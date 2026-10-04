# Lanternvale — Rules Engine API (`Lanternvale.Rules`)

Pure C# 9 (no UnityEngine) in `Assets/Lanternvale/Scripts/Core/Rules/**`, one namespace: `Lanternvale.Rules`.
It interprets the data in `Resources/Data` (see `DataSchema.md`, `Design.md`) and is driven by the session layer
(`Lanternvale.Session`) and the Unity presentation layer. All randomness goes through `Lanternvale.Util.Rng`.

> Units: positions/distances in **metres** (`Vec2`), data ranges in yards are converted with `MathUtil.Yd`
> (1 yd = 0.4 m). Durations/cooldowns in **seconds**. One combat round = 6 s.

| Area | Main types |
|---|---|
| Units & stats | `Unit`, `UnitKind`, `UnitRole`, `UnitStats`, `StatBlock`, `StatCalculator`, `CreatureScaling`, `UnitFactory`, `WeaponInfo` |
| Abilities | `AbilityRules`, `AbilityMods`/`AbilityModSet`, `Targeting`/`AreaShapeInfo`, `Tooltip`, `AbilityStatus`, `UseCheck`/`UseFailure` |
| Combat | `Battle` (+ `CombatEvent`, `CombatLog`, `BattleResult`, `BattleOutcome`, `UnitMeters`, `AbilityCast`) |
| AI | `AI` (`NextStep`, `Execute`, `RunTurn`), `AIStep` |
| Items | `ItemInstance`, `Inventory`, `Equipment`, `EquipmentRules`, `ItemGenerator`, `LootGenerator`, `LootDrop`, `VendorShop` |
| Progression | `Progression` (XP, levels, talents, respec, trainers), `LevelUpInfo`, `TrainerOffer` |
| Pathing | `IPathfinder`, `PathResult`, `StraightLinePathfinder` (tests); the game uses `Lanternvale.Session.NavGridPathfinder` |
| Specials | `Specials` (registry, hooks, `RunContentSpecial`, `FieldEvent`), `SpecialHandler`, `IContentContext` |

---

## 1. Units

```csharp
// characters / companions (abilities: class startingAbilities + basics; learnAll = every rank up to level)
Unit hero = UnitFactory.CreateCharacter(db, ClassId.Mage, "Yuna", level: 1, learnAll: false, leftovers: bagList);
Unit comp = UnitFactory.CreateCompanion(db, db.Companions["lys"], level: 12);   // learns all + auto talents
Unit wolf = UnitFactory.CreateCreature(db, db.Creature("cr_wolf"), level: 5, team: Team.Enemy);
int lvl   = UnitFactory.CreatureLevel(def, explicitLevel, partyLevel, rng);       // scaleToParty + levelOffset
```

Key `Unit` members:

* Identity: `Id` (unique int, also the nav occupancy id), `Name`, `Kind` (Character, Companion, Creature, Pet,
  Totem, Summon), `Team`, `Level`, `Xp`, `Class`/`Creature`/`Companion` defs, `IsMainCharacter`, `AutoPlay`
  (companion AI controls it), `RoleOverride` / `Role` (Tank, Healer, MeleeDps, RangedDps; inferred from talents).
* Position: `Position`, `Facing` (unit vector), `Radius`.
* Resources: `Health`, `Mana`, `Rage`, `Energy`, `Focus`, `MaxHealth`, `MaxMana`, `MaxResource(type)`,
  `PowerType`, `HealthPct`, `GetResource/SetResource`, `RestoreFull()`.
* Knowledge: `Abilities` (id → rank), `Talents` (id → rank), `Knows(id)`, `RankOf(id)`, `TalentRank(id)`.
* `Equipment` (paper doll, see §6), `Auras` (`List<AuraInstance>`), `Cooldowns` (id or `grp:<group>` → s),
  `CooldownLeft(abilityDef)`, `Lockouts` (school → s).
* Combat state: `AttackTarget`, `AutoAttacking`, `AutoAttackAbility`, `QueuedSwing`, `ComboPoints`, `ComboTarget`,
  `Threat` (AI units: unit → threat), `AggroTarget`, `Pending` (`PendingCast`), `TimeLeft`, `TimeDebt`,
  `MoveLeft`/`MoveBudget` (metres), `TurnsTaken`, `Reactive` windows, `Downed`, `Dead`, `IsAlive`.
* Pets: `Owner`, `Pet`, `Totems` (element → unit), `Summons`, `Lifetime`, `HunterPet` (`HunterPetState`: template,
  name, remembered health, dead flag — persist it in saves).
* Specials' per-unit scratch: `Vars` / `GetVar` / `SetVar` (combat-transient, not saved), `MaxHealthMult`
  (Earth's Grasp totems), `ExtraAttacks` (Reckoning, cleared at battle end), `SelfRes` (pending self-resurrection
  offer), `FacingLock` (Distract), `IsFeigningDeathFor(observer)` (enemies that resisted Feign Death see through it).
* State queries: `HasState(UnitState)`, `IsControlled`, `IsStealthed`, `IsRooted`, `IsInvulnerable`, `IsBehind(t)`,
  `HasAura(id, caster?)`, `FindAura`, `HasAuraWithTag`.
* `Stats` (`UnitStats`, cached; call `InvalidateStats()` after manual changes): `Strength…Spirit`, `MaxHealth`,
  `MaxMana`, `Armor`, `AttackPower`, `RangedAttackPower`, `HealingPower`, `SpellDamage(school)`,
  `MeleeCrit`, `RangedCrit`, `SpellCrit(school)`, `Dodge`, `Parry`, `BlockChance`, `BlockValue`, `Defense`,
  `MeleeHit`, `RangedHit`, `SpellHit(school)`, `Resistance(school)`, `MoveSpeedPct`, `MeleeHaste`/`RangedHaste`/
  `CastSpeed` (multipliers), `ManaRegen` (MP5), `HealthRegen` (HP5), `SpiritRegenWhileCasting`,
  `SpiritRegenPerTick`, `DamageDone(school)`, `DamageTaken(school)`, `Threat(school)` …
* `StatCalculator.GetWeapon(unit, WeaponSlot)` → `WeaponInfo` (real item, fists, or the creature's virtual weapon).

Stats follow Design.md §3: class base stats interpolate between levels 1 and 60, + companion `statBonus`, + items
(stats, armor, block, suffix stats, `equipEffects` Stat passives), + talents (`Stat` passives; `target: "Pet"` ones
apply to the pet), + auras (`mods` × stacks; `values[rank-1]` or `value + perLevel × (rankLevel − learnLevel)`
of the applying ability, × its `Effect` AbilityMod), + special passives.

### Creature curves (`CreatureScaling`)

Normal creature at level L (before `healthMult/damageMult/armorMult/manaMult`):
Health `42 + 16.5(L−1) + 0.75(L−1)²` (L10 251, L20 626, L30 1151, L60 3626); melee DPS `(1 + L + 0.03L²)/2`
(one swing = DPS × attackSpeed); Armor `20L + 0.5L²`; Mana `60 + 25L + 0.4L²` (resource Mana only); primary
stats `15 + 1.5L`. **Rank multipliers** (the content's `healthMult/damageMult/armorMult` apply on top): health
Elite/Rare ×3, Boss ×7, Minion ×0.5, Critter ×0.2; damage Elite/Rare ×1.5, Boss ×2, Minion ×0.6, Pet ×0.75, Critter
×0.1; armor Critter ×0.5 (the Hollow Warden at party level 12 → L14 → ≈3,085 health). Totems have `5 + 3L` health.
Creatures crit 5%, dodge 5%,
parry 5% (not beasts/elementals/mechanicals/totems; not pets). Pets, demons, totems and summons take their
owner's level and only know creature abilities whose `learnLevel` ≤ their level.

`UnitFactory.AttachAura(unit, def, caster, passive, rank, effLevel, learnLevel, remaining)` adds an aura without events
(passives, save restore); `UnitFactory.RefreshModValues(aura)` (public) recomputes its stat values from data and
re-applies special scaling (Master Demonologist × talent rank, Expose Armor × combo points) — call
`unit.InvalidateStats()` afterwards. `UnitFactory.AttachPassives(unit)` (re)attaches passive-ability auras.

---

## 2. Battle lifecycle

```csharp
var battle = new Battle(db, rng, pathfinder, partyInventory);   // inCombat: true
battle.AddUnits(partyAndPets);  battle.AddUnits(enemies);       // set Position/Team first
battle.Begin();                          // or Begin(Team.Enemy) = enemies surprised (lose their first turn)
// or: battle.BeginWithOpener(rogue, "rogue_cheap_shot", target)   // ability before combat (Charge, openers);
//     opening from stealth with an Opener-tagged ability surprises the enemies
```

After `Begin` the battle stops at the first unit that can act: `battle.ActiveUnit`. Turn processing (start of turn:
6 s elapse for its auras/cooldowns, DoT/HoT ticks, regen with five-second rule, pending cast resolution, control
effects; end of turn: auto attacks via swing timers, totem actions) happens inside the engine. Stunned/surprised
units are skipped automatically (events `TurnStart` + `TurnSkipped` + `TurnEnd`).

* `battle.NeedsPlayerInput` — active unit is player-controlled; else `battle.IsAIControlled(unit)` → use §4.
* Player actions on the active unit (all return `ActionResult { Ok, Reason }`):
  * `UseAbility(unit, abilityId, targetUnit = null, point = null)` — ability by id (auto attacks toggle; `nextSwing`
    abilities queue; casts that do not fit the remaining Time become **pending** and end the turn).
  * `UseItem(unit, itemInstance, target, point)` — item `use` ability; consumes one if `consumable`.
  * `Move(unit, destination)` / `MoveAlong(unit, pathPoints)` — truncated to `MoveLeft`; `PreviewMove(unit, dest)`
    returns the `PathResult` for UI previews; `CannotMoveReason(unit)`.
  * `Wait(unit, seconds)`, `StartAutoAttack(unit, target, basicAbilityDef, toggle)`, `StopAutoAttack(unit)`,
    `CancelQueuedSwing(unit)`, `CancelAura(unit, aura)` (own removable buffs: Ice Block, Phase Shift…).
  * `EndTurn(unit)` — end-of-turn processing, then the next turn starts (may run AI-less skipped turns).
* Downed party members with a **self-resurrection** (Soulstone, Reincarnation) get a turn slot:
  `PendingSelfResurrection(unit)` → `SelfResOffer { Source, Name, Health, Mana }`; `AcceptSelfResurrection(unit)` /
  `DeclineSelfResurrection(unit)` (AI-controlled units accept automatically). Reincarnation needs `shaman_ankh` in the
  party inventory and the `shaman_reincarnation` cooldown (saved in `Cooldowns`).
* Side changes: Mind Control (`priest_mind_control`) and Enslave Demon move a creature to the party
  (`ChangeSide`/`RestoreSide`; `unit.OriginalTeam` is set while converted). A mind-controlled creature is controlled
  by the player (its `Owner` is the priest; `NeedsPlayerInput` when the priest is player-controlled) and still counts
  as an enemy for victory; any action/move/CC of the priest ends it. Enslaved demons are real pets and are released
  (removed) when the battle ends.
* Divine Intervention: the paladin dies outright; a party whose only standing member is banished+invulnerable loses.
* Ending: victory when no hostile non-totem unit is alive (mind-controlled enemies still count as hostile), defeat
  when no party character is up.
  `battle.IsOver`, `battle.Outcome` (`Victory`, `Defeat`, `Fled`), `battle.Result` (`BattleResult`: `Xp[unit]`,
  `Defeated`, `Loot` = items + gold; **not applied** — the session grants XP/loot), `battle.KilledCreatures`
  (creature ids for quest objectives). Downed allies stand up at 1 HP after a victory; player totems/temporary
  summons despawn; persistent pets stay.
* Passive encounters (training dummy): `battle.CanDisengage` / `battle.Disengage()` (outcome `Fled`).
* Rounds: `battle.Round`, `battle.TurnOrder` (initiative order, pets right after their owner), `battle.Meters[unit]`.

### Usability, previews, action bar

```csharp
UseCheck c = battle.CanUse(unit, "mage_fireball", target, point);   // c.Ok / c.Code (UseFailure) / c.Reason (UI text)
UseCheck c2 = battle.CanUseIgnoringTarget(unit, abilityDef);         // for greying out bar buttons
List<AbilityStatus> bar = battle.GetAbilityBar(unit);               // known non-passive, non-hidden (+ contextual, e.g. Lightwell renew)
// AbilityStatus: Ability, Rank, MaxRank, Usable, Code, Reason, Cost, CostType, TimeCost, CastTime,
//                Cooldown, CooldownLeft, NeedsTarget, NeedsPoint, Active (stance/aura on, auto attack, queued), Tooltip
AreaShapeInfo shape = Targeting.AreaOf(unit, ability, target, point, AbilityMods.For(unit, ability)); // circle/cone/line
List<Unit> hit = Targeting.AreaUnits(battle, unit, ability, target, point, mods);
bool seen = battle.CanSee(observer, target);       // stealth
float reach = battle.MeleeReachOf(a, b); bool inMelee = battle.InMeleeRange(a, b);
```

Reasons are complete sentences ("Not enough rage (15).", "You must be behind your target.", "Requires Battle
Stance.", "Target is too close.", "It is not your turn." …).

Ability numbers: `AbilityRules.ResourceCost(unit, ability, rank, mods)`, `TimeCost`, `CastTime`, `Gcd`,
`Cooldown`, `RangeMetres`, `MinRangeMetres`, `RadiusMetres`, `KindOf` (Melee/Ranged/Spell/Wand hit table),
`EffLevel` (rank level, or unit level for creatures/`scaleWithLevel`), `RankCount`, `MaxRankAtLevel`.

### Events

Every visible change is a `CombatEvent` appended to `battle.Events` and raised on `battle.EventRaised`.
Read incrementally: `var list = battle.TakeEvents(ref cursor);`. Fields: `Type`, `Round`, `Source`, `Target`,
`AbilityId`, `AuraId`, `Name`, `School`, `Amount`, `Overkill`, `Overheal`, `Absorbed`, `Resisted`, `Blocked`,
`Crit`, `Periodic`, `OffHand`, `Ranged`, `AutoAttack`, `Resource`, `From`, `To`, `Path`, `Seconds`, `Count`,
`Reason`, `Text` (ready combat-log line; `CombatLog.Format(e)`).

Types: BattleStart, BattleEnd, RoundStart, TurnStart, TurnEnd, TurnSkipped, Damage, Heal, Miss, Dodge, Parry,
Block, Resist, Absorb, Immune, Evade, AuraApplied, AuraRefreshed, AuraRemoved, AuraStack, AuraBroken, Dispel,
AbilityUsed (instant uses; Reason = item name for items), CastStart (Seconds = cast time; Reason
"channel"/"continuing"), CastComplete, CastInterrupted,
CastFailed, ChannelTick, SwingQueued, AutoAttackToggled, ResourceChange, ComboPoints, Move (Path), Teleport,
Charge, Knockback, Summon, Despawn, Death, Downed, Revive, Threat, Taunt, TargetChanged, ItemCreated,
ItemConsumed, CooldownReset, Initiative, Log.

### Out of combat ("field")

```csharp
var field = Battle.CreateField(db, rng, partyUnits, pathfinder, inventory);  // InCombat = false
field.UseAbility(priest, "priest_power_word_fortitude", warrior);           // instant, no turns/Time
field.UseItem(mage, waterItem, mage);                                        // food/drink (notInCombat) etc.
field.TickOutOfCombat(deltaSeconds);    // real time: cooldowns, buff expiry, HoT/food ticks, regen, summon lifetimes
field.AddUnit(newPet) / RemoveUnit(unit)
```

Out-of-combat regeneration per second: health 2% max + 0.25 × Spirit (+HP5/5); mana: Spirit regen (2 s tick / 2)
+ 1% max outside the five-second rule (only the SpiritRegenWhileCasting share inside it) + MP5/5; energy +10,
focus +6, rage −1. Specials also tick out of combat (Spirit Bond heals per 10 s).

`Specials.FieldEvent` (`Action<Battle, Unit, string, Unit>`) is raised for specials whose effect is outside combat
rules: `"RoguePickLock"` (the Pick Lock cast completed; the session resolves the lock — `RoguePickLock.Roll(rogue,
dc, skillModifier, rng, out total)` is available) and `"PriestMindSoothe"` (target creature; the session shrinks that
encounter's trigger radius by 4 m while the creature has `priest_mind_soothe`).

---

## 3. Combat rules summary (what the engine does)

* Time: `Gcd` abilities cost max(cast, GCD); `OffGcd` cost the cast time. Instants overflowing the remaining Time
  create **time debt**; casts/channels that do not fit become **pending** (turn ends) and resolve at the start of
  the next turn (`Pending.RemainingTime`, >6 s casts span several turns). Channels resolve ticks proportionally
  (effects are per tick). **Casting pushback**: damage taken by a unit with a pending cast adds 0.5 s (channels
  lose 0.5 s), max twice per cast; talents/auras resist or reduce it.
* Control: at turn start the remaining duration of control auras (stun, incapacitate, sleep, polymorph, banish,
  fear, confuse) **costs that many seconds of the turn's Time** (≥ 6 s = turn skipped; fear/confuse auto-move);
  roots remove that share of movement; silences and school lockouts block during that many seconds of the turn
  clock (`Wait` lets them run out).
* Hit tables: white swings use one roll (miss incl. +19% dual-wield, dodge, parry/block frontal only, crit);
  abilities roll avoidance then crit per effect; spells miss 4%/5%/6%/15%+ by level difference; partial
  resistance is average mitigation. Armor `a/(a + 400 + 85·L)` ≤ 75%. Crits ×2 melee/ranged, ×1.5 spells/wands.
* Auto attacks at end of turn: timer += controlled seconds × haste, one swing per full weapon speed; Heroic
  Strike/Cleave/Raptor Strike replace the next main-hand swing; off hand 50%.
* Rage: dealing white damage `7.5d/c × 1.75`, taking `2.5d/c`; avoided rage abilities refund 80%.
* Threat: damage 1:1 (+ bonus `threat`), healing 0.5 split among engaged enemies, mana 0.5/rage 5 per point;
  tag `NoThreat` disables it. AI targets top threat, switching at 110% (melee) / 130% (ranged). Taunt forces the
  next turn's target and matches the top threat. Totems/traps credit their owner.
* Procs fire from auras (scaled by the applying ability: rank, EffLevel, its AbilityMods and combo points),
  talents (`Proc` passives; Rank = talent rank, EffLevel = unit level, LearnLevel = 1) and items (no scaling). Hit procs (OnMeleeHit/OnRangedHit/OnSpellHit) fire **once per target per cast / channel tick** after the
  cast's effects (and per white swing); auras applied by a cast are not consumed by that same cast's procs.
* Area auras (`radius` + `radiusAura`) are maintained around their bearer (paladin auras include the paladin).
* `RemoveAura` with `auraTag` also matches auras whose `states` include that UnitState name.

---

## 4. AI (one step at a time)

```csharp
while (!battle.IsOver && battle.ActiveUnit != null && battle.IsAIControlled(battle.ActiveUnit))
{
    AIStep step = AI.NextStep(battle, battle.ActiveUnit);   // Move(path) | UseAbility(id, target/point) | EndTurn
    // animate step (step.Path, step.AbilityId, step.Target, step.Point, step.Reason)
    AI.Execute(battle, step);                               // performs it (failures are remembered)
}
AI.RunTurn(battle, unit);                                    // whole turn at once (tests / fast-forward)
```

* Enemies: `CreatureDef.ai` profile + `abilities` (priority, chance per turn, `condition`:
  `selfHpBelow:N`, `selfHpAbove:N`, `allyHpBelow:N`, `targetHpBelow:N`, `targetCasting`, `targetNoAura:id`,
  `selfNoAura:id`, `selfAura:id`, `enemiesInRange:N`, `hasPet`, `noPet`, `roundAtLeast:N`, joined with `&`),
  threat targeting, big casts (≥ 2.5 s) started last so they telegraph as pending casts.
* Pets follow the owner's target (tank pets pick up loose enemies) and use their creature abilities by `priority`,
  `chance` (per turn; 0 = never) and `condition`, then anything else they know by `aiPriority`.
* Companions (`unit.AutoPlay = true`): role-aware scoring of every known ability with `aiHint`/`aiPriority`
  (heals by deficit, buffs/debuffs kept up, interrupts, taunts by tanks, threat cap for DPS, finishers at 5 combo
  points, totems per element, stances/seals/aspects by role, hunters out of the dead zone, casters out of melee).
  Pointless uses are skipped (DoT/curse/sting already running, mana drains on manaless targets, dispels with nothing
  to dispel, snares on immobile targets); contextual heals (Lightwell) are taken below 70% health.

---

## 5. Specials

`Specials.IsImplemented(name)`; `Specials.Register(handler)`; `Specials.Get(name)`; `Specials.Names`. Every special
documented in `classes/*.json` and `content/config.json` is implemented (123 names; the harness test
`TestsRulesCore.EveryDocumentedSpecialIsImplemented` checks it). Source: `Rules/Specials/Specials.<Class>.cs`.

A handler derives from `SpecialHandler` and overrides only the hooks it needs. The same name may be used as an
ability `special`, an effect (`type: Special`, or `special` on any other effect = modifier), an aura `special`, or a
talent/item `Special` passive. Hook families:

* ability-level: `CheckUse`, `ValidateTarget`, `TimeCost`, `BeforeUse`, `Resolve` (set `SkipEffects` to replace the
  effects), `After`, `ModifyDamage/Healing/ResourceGain` (also applied to ticks of auras the ability applied),
  `ThreatMultiplier`, `Unavoidable`;
* effect-level: `Execute`, `ReplaceEffect`, `ModifyAuraDuration`, `AfterAuraEffect`, `Unavoidable`, `CheckUse`;
* aura-level: `OnAuraApplied/Refreshed/Removed`, `OnAuraTick`, `OnBearerTurnStart`, `ModifyIncomingDamage/Heal`,
  `IncomingCritBonus`, `IncomingFlatDamageBonus`, `IncomingFlatHealBonus`, `IncomingRangedApBonus`, `SkipAbsorbPool`,
  `RevealsTo`, `RedirectSpell`, `OnHolderDowned`, `MovementTriggerRadius/OnMovementTrigger`, `OnBearerDamaged`,
  `PreventsFleeing`;
* passive (talents, items, and auras carrying the special): stats (`ContributeStats`, `AdjustStats`), ability mods,
  cost/cast time, crit (`ModifyCritChance`, `ModifyIncomingCritChance`, `CritBonusAdd`), procs (`OnProc`,
  `ProcChanceBonus`), resists (`ResistIncomingAura`, `InterruptResistChance`, pushback resist/reduction), reflect,
  `PreventDeath`, `GrantsWeapon`, `CannotUse`, `DetectionRadiusMult`, `OnPetChanged`, `OnOwnerSummoned`, …
  (`Specials.CurrentPassive` / `CurrentSource` tell a shared handler which talent it serves);
* global: `OnAnyTurnStart`, `OnAnyUnitMoved`, `OnAnyAuraApplied/Removed`, `OnAnyUnitFell`, `OnAnyAbilityStart`,
  `OnBattleFinished`, `OnOutOfCombatTick`, `OnModValuesRefreshed`, `ContextualAbilities`.

`Specials.ContextualAbilities(battle, unit)` lists abilities a unit may use because of its surroundings (Lightwell
renew within 5 yd of a Lightwell with charges). `Specials.UndetectableAuras` (Vanish) and
`Specials.PushbackImmuneAuras` (Power Word: Shield) are id sets.

**Content specials** (dialogue/encounter outcomes) run through the session:
`Specials.RunContentSpecial(name, IContentContext ctx)` → false if unknown. `IContentContext` = `GetFlag`, `SetFlag`,
`RaiseEvent(name, arg)`. `RekindleLanterns` sets flag `lanterns_rekindled` and raises event `"RekindleLanterns"`.

---

## 6. Items, equipment, inventory, loot, vendors

```csharp
var inv = new Inventory { Gold = 1000 };          // shared party bags (copper); soul_shard capped at 32
inv.Add(db.Item("potion_minor_healing"), 3); inv.Count("soul_shard"); inv.Remove("soul_shard", 1);
inv.Changed += (itemId, total) => quests.OnItemCount(itemId, total);
string why = EquipmentRules.CannotEquipReason(unit, itemDef, EquipSlot.MainHand);   // null = OK
EquipSlot? slot = EquipmentRules.ChooseSlot(unit, itemDef);
List<ItemInstance> displaced = EquipmentRules.Equip(unit, item, slot.Value);       // take item out of bags first
ItemInstance off = EquipmentRules.Unequip(unit, EquipSlot.OffHand);
```

Rules: armour by class `armorTypes` (+ `armorUpgrade` at its level; lower types allowed), weapons by
`weaponTypes` (shields need Shield or `canBlock`), dual wield from `dualWieldLevel`, two-handers clear the off hand,
`requiredLevel`, `classes`, `unique`.

* `ItemInstance`: `Def`, `Count`, `Name` (with suffix), `SuffixId/SuffixName/SuffixStats`, `Generated` (def not in
  the database — save the whole `Def`), `SellPrice` (¼ price), `Uid`.
* `ItemGenerator.RandomItem(db, rng, itemLevel, quality)`, `MakeDef`, `ApplyRandomSuffix`, `StatBudget`,
  `ArmorValue`, `WeaponDps`, `VeteranGear(db, unit, rng)` / `EquipVeteranGear(db, unit, rng)` (level-appropriate set
  for characters created above level 1). Generated ids are `gen_<slot>_<ilvl>_<n>`; after loading a save call
  `ItemGenerator.EnsureCounterAbove(loadedItemIds)` (or `EnsureCounterAbove(int)`) so new ids never collide;
  `ItemGenerator.GeneratedCounter` is the current value.
* Weapon proficiency also comes from talents (`EquipmentRules.CanUseWeapon` asks `Specials`: Two-Handed Axes and
  Maces). Class reagents live in the party inventory: `soul_shard` (max 32), `shaman_ankh`, `rogue_flash_powder`,
  `rogue_blinding_powder` (checked only for the player's party).
* `LootGenerator.Roll(db, lootTableId, level, rng)` → `LootDrop { Items, Gold }`.
* `new VendorShop(db, npcDef, stockDict, buybackList)`: `Offers()`, `Buy(inv, itemId, n)`, `Sell(inv, item, n)`,
  `BuyBack(inv, item)` (return null on success, else a reason).

---

## 7. Progression

```csharp
int xp = Progression.KillXp(db, charLevel, mobUnit);       // WoW formula × elite/boss × xpMult × config.xpRate
List<LevelUpInfo> ups = Progression.GiveXp(unit, amount);   // every party member gets full kill XP
Progression.QuestXp(db, amount); Progression.XpToNextLevel(db, level); Progression.SetLevel(unit, level);
// talents: 1 point per level from 10; tier gate 5×(tier−1) in the tree; arrows must be maxed
string why = Progression.CannotLearnTalent(unit, talentId); Progression.LearnTalent(unit, talentId);
Progression.TalentPointsAvailable(unit); Progression.PointsInTree(unit, treeId);
Progression.AutoAllocateTalents(unit);       // CompanionDef.preferredTalents or ClassDef.defaultBuild
int cost = Progression.RespecCost(unit);     // 1g, 5g, 10g, 15g ... 50g; then:
List<ItemInstance> unequipped = Progression.ResetTalents(unit, bags);   // items no longer usable go to `bags`
// trainers
List<TrainerOffer> offers = Progression.TrainerOffers(unit, inv);  // CanTrain / Reason ("Requires level 24.")
string why2 = Progression.Train(unit, abilityDef, rank, inv);     // pays rank costs (rank 1 = trainCost, later max(trainCost, 4·lvl²))
Progression.LearnAllAvailable(unit);         // veteran start
```

---

## 8. Tooltips

`Tooltip.Ability(unit, ability)` replaces `{0}`, `{1}` (effect magnitudes at the unit's rank/stats, "14 to 22")
and `{d0}` (aura duration). `Tooltip.AbilityFull(unit, ability)` adds name/rank/cost/range/cast/cooldown lines.
`Tooltip.Talent(talentDef, rank, onlyCurrent:false)` renders `{a/b/c}` groups with the current rank wrapped in
`Tooltip.HighlightOpen/HighlightClose` (IMGUI rich text by default).

---

## 9. Pathfinding adapter

The engine uses `IPathfinder` (`FindPath`, `FindPathToRange`, `IsWalkable`, `HasLineOfSight`, `ClampToWalkable`,
`SetUnit(id, pos, radius ≤ 0 removes)`, `ClearUnits`). `StraightLinePathfinder` is an open-field implementation for
tests; the game passes `Lanternvale.Session.NavGridPathfinder` (World `NavGrid` adapter).

---

## 10. What to save (rules state)

Per unit: level, xp, abilities (ranks), talents, equipment, health/mana/resources, `Cooldowns` (incl. long ones such
as `shaman_reincarnation`), `HunterPet`, `RespecCount`, and auras that persist out of combat (buffs, passives, stances,
Soulstone, Master Demonologist) with `Rank`, `EffLevel`, `LearnLevel`, `ComboPoints`, `EffectMult`/`DamageMult`/
`HealingMult`, `Remaining`, `Stacks`, `Charges`, `AbsorbLeft`, caster; restore with `UnitFactory.AttachAura` +
`RefreshModValues`. Party `Inventory` (incl. reagents and `Generated` item defs). Not saved (combat-transient):
`Unit.Vars`, aura `Vars`/`ExtraMods`/proc cooldowns, `Unit.ProcCooldowns` (the only data ICD is the Lanternbough's
30 s spell proc; losing it on load at most lets it proc once early), threat, combo points, pending casts.
