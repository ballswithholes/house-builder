# Lanternvale — Data Schema Guide

All game data lives in `Assets/Lanternvale/Resources/Data/**.json`. Every file is a **DataBundle** — an
object with any of these arrays: `classes`, `abilities`, `auras`, `talentTrees`, `items`, `itemSuffixes`,
`lootTables`, `creatures`, `npcs`, `companions`, `dialogues`, `quests`, `maps`, `specials`, and an optional
`config` object. The loader merges all files. **The C# definitions in
`Scripts/Core/Data/Defs.cs` and `Enums.cs` are the source of truth** — field names are JSON keys, enums are
written by name (case-insensitive). Unknown keys are reported as errors (prefix a key with `_` for comments,
e.g. `"_note": "..."`). `//` comments and trailing commas are allowed.

Validate with: `Lanternvale/Tools/check.sh core` (must print `Data validation: OK`).

## Units and conventions

| Quantity | Unit |
|---|---|
| range, radius, distance | WoW **yards** (engine converts 1 yd = 0.4 m) |
| durations, cooldowns, cast times, tick intervals | **seconds** (1 combat round = 6 s) |
| money | **copper** (100c = 1s, 100s = 1g) |
| levels | 1–60 (WoW Classic) |
| magnitudes | WoW Classic numbers at the ability's **first rank** |

**IDs** are lowercase snake_case, prefixed by class: `mage_fireball`, `mage_fireball_dot` (aura),
`mage_fire_improved_fireball` (talent), `tree_mage_fire` (talent tree).

## Ranks and scaling

WoW abilities have ranks. Give `rankLevels` (ascending, first = `learnLevel`) and author magnitudes for
**rank 1**, plus `perLevel` on each effect so that `value(rankLevel) = min + perLevel × (rankLevel − learnLevel)`
approximates the real max rank. Costs scale the same way via `cost.perLevel`.

Example: Fireball R1 (lvl 1) 14–22 dmg, 30 mana; R11 (lvl 56) 561–715, 410 mana →
`min 14, max 22, perLevel 10.4` (≈ (638−18)/(56−1) ≈ 11.3 average; pick the value that lands max rank near
the real one), `cost.amount 30, cost.perLevel 6.9`.

## Abilities (`AbilityDef`)

```jsonc
{
  "id": "mage_fireball", "name": "Fireball", "icon": "fireball", "classId": "Mage", "school": "Fire",
  "description": "Hurls a fiery ball that causes {0} Fire damage and an additional {1} Fire damage over 8 sec.",
  "learnLevel": 1, "rankLevels": [1,6,12,18,24,30,36,42,48,54,60], "trainCost": 10,
  "castTime": 3.5, "range": 35,
  "cost": { "type": "Mana", "amount": 30, "perLevel": 6.6 },
  "target": "Enemy",
  "effects": [
    { "type": "Damage", "min": 14, "max": 22, "perLevel": 11.0, "coef": 1.0 },
    { "type": "ApplyAura", "aura": "mage_fireball_dot" }
  ],
  "aiHint": "Damage", "aiPriority": 5
}
```

Key fields (see Defs.cs for all):

* `time`: `"Gcd"` (default, costs max(castTime, GCD) seconds of the 6 s turn) or `"OffGcd"` (costs castTime only).
* `castTime`, `channeled` + `channelTicks`, `cooldown`, `cooldownGroup` (shared cooldown, e.g. `"shock"`).
* `cost`: `{ "type": "Mana|Rage|Energy|Focus", "amount", "perLevel", "pctBaseMana", "consumeAll", "consumesComboPoints" }`.
* `target`: `Self | Enemy | Ally | AllyOther | Point | Any | DeadAlly | Pet`.
* `range` / `minRange` (yards), `melee: true` for melee reach.
* `area`: `{ "shape": "Circle|Cone|Line", "radius", "angle", "width", "centeredOnCaster", "maxTargets", "affects": "Enemies|Allies|All" }`.
  With an area, effects whose `target` is `Target` hit every unit in the area.
* `requires`: `casterAuras`, `casterAuraTags`, `targetAuras`, `targetAuraTags`, `targetAuraFromSelf`,
  `casterStates` (e.g. `["Stealth"]`), `notInCombat`, `inCombat`, `behindTarget`, `mainHand` (weapon type
  names), `shield`, `rangedWeapon`, `meleeWeapon`, `dualWield`, `targetHealthBelowPct`,
  `targetCreatureTypes`, `reactive` (`TargetDodged`, `TargetParried`, `SelfDodged`, `SelfParried`,
  `SelfBlocked`, `SelfDodgedParriedBlocked`, `SelfCrit` — window lasts until the end of the unit's next turn),
  `hasPet`, `noPet`, `minComboPoints`, `outOfMeleeRange`, `minHealthPct`.
* `tags`: free-form but use the common ones: `Interrupt Finisher Opener Builder Totem Seal Judgement Aura
  Blessing Stance Sting Aspect Curse Shock Armor Poison Heal Shout Trap Pet Shapeshift Form Conjure Buff Taunt`.
* `exclusiveGroup`: using the ability removes the caster's auras of that group first (stances, aspects…).
  Normally put `exclusiveGroup` on the **aura** instead; the engine enforces aura groups on application.
* `autoAttack: true` for basic attacks (`attack`, `auto_shot`, `shoot`); `nextSwing: true` for Heroic Strike,
  Cleave, Raptor Strike (they replace the next main-hand auto attack and cost Time 0).
* `generatesComboPoint: true` for rogue builders.
* `passive: true` + `passiveAura` for always-on abilities (Dual Wield, Parry, Plate Mail proficiency…).
* `fromTalent: true` for abilities granted by talents (they still need full data).
* `special`: name of a code handler for truly unique mechanics. Every special must also be documented in a
  `specials` array entry: `{ "id": "Execute", "usedBy": "warrior_execute", "behaviour": "exact rules" }`.
* `aiHint` (`Damage Heal Buff Debuff Interrupt Defensive Finisher Opener AoE Taunt Summon CC Utility`) and
  `aiPriority` (0–10) guide companion auto-play and enemy AI.

### Description tokens

`{0}`, `{1}`… are replaced by the computed magnitude of `effects[0]`, `effects[1]`… at the caster's current
rank and stats (min–max range for damage/heal). `{d0}` = duration of the aura applied by effects[0].

## Effects (`EffectDef`)

`type` (EffectType) and the fields it reads:

| type | fields |
|---|---|
| `Damage` | `min max perLevel perCombo coef apCoef school pctOfDamage cannotCrit cannotMiss` |
| `WeaponDamage` | `weaponPct` (% of weapon roll incl. AP), `min max perLevel` (flat bonus), `offHand ranged` |
| `Heal` | `min max perLevel coef pctOfMax` |
| `ApplyAura` | `aura stacks duration durationPerCombo chance` |
| `RemoveAura` | `aura` or `auraTag` |
| `Dispel` | `dispelType dispelCount` (on enemies removes buffs, on allies removes debuffs) |
| `Interrupt` | `lockout` (seconds the interrupted school is locked) |
| `Taunt` | — |
| `Threat` | `threat` (flat) or `threatPct` (−100 wipes, 50 = +50%) |
| `GainResource` | `resource amount perLevel pctOfMax` (resource: Mana Rage Energy Focus Health ComboPoints) |
| `DrainResource` | `resource amount perLevel pctToCaster` |
| `Teleport` | `distance` (forward, Blink) or to the Point target |
| `Charge` | moves caster next to target |
| `Knockback` | `distance` |
| `Summon` | `summon lifetime count` (creature id) |
| `SummonTotem` | `summon totemElement lifetime` |
| `Resurrect` | `pctOfMax` |
| `CreateItem` | `item count` |
| `ResetCooldowns` | `abilities tags schools` filters |
| `TriggerAbility` | `ability` |
| `Kill` | — |
| `Special` | `special` |

Common to all: `target` (`Target Self Area Pet Owner AlliesInRadius EnemiesInRadius Party Attacker`),
`radius` (for *InRadius), `chance`, `threat` (bonus threat), `requireTargetAura` + `consumeTargetAura`,
`chainTargets chainFalloffPct chainRange`.

## Auras (`AuraDef`)

```jsonc
{
  "id": "mage_fireball_dot", "name": "Fireball", "icon": "fireball", "kind": "Debuff", "school": "Fire",
  "dispel": "Magic", "duration": 8, "tickInterval": 2,
  "tickEffects": [ { "type": "Damage", "min": 1, "perLevel": 0.9 } ]
}
```

* `kind` Buff/Debuff, `dispel` None/Magic/Curse/Poison/Disease, `duration` seconds (≤ 0 = until removed),
  `maxStacks`, `charges`, `exclusiveGroup` + `exclusivePerCaster`, `tags`, `hidden`.
* `mods`: `[{ "stat": "Armor", "value": 25, "pct": true, "school": "Fire" }]` (StatId list in Enums.cs).
  There is no separate Focus stat: `EnergyRegen` (pct) scales both Energy and Focus regeneration (pets: a talent
  `Stat` passive with `"target": "Pet"`, e.g. Bestial Discipline).
* `states`: UnitState names (`Stun Root Silence Pacify Disarm Fear Polymorph Incapacitate Sleep Confuse Stealth
  Invisible Invulnerable ImmunePhysical ImmuneMagic Banish Shapeshift FeignDeath Daze Untargetable`).
* `forbidSchools`, `absorb` (`{ "amount", "perLevel", "coef", "schools", "manaPerDamage" }`),
  `breakOnDamage` + `breakDamageThreshold`, `breakOnAction`, `breakOnMove`.
* `tickInterval` + `tickEffects` (magnitudes **per tick**), `onApply`, `onExpire`, `onRemove`.
* `procs`: `[{ "trigger": "OnMeleeHit", "chance": 20, "ppm": 0, "abilities": [], "tags": [], "schools": [],
  "effects": [...], "consumeCharge": true, "internalCooldown": 0 }]`.
  Triggers: `OnMeleeHit OnAutoAttackHit OnRangedHit OnSpellHit OnSpellCast OnHealCast OnCrit OnMeleeCrit
  OnSpellCrit OnStruck OnDamaged OnCritTaken OnDodge OnParry OnBlock OnTargetDodged OnTargetParried OnKill
  OnFinisher OnAbilityUsed OnTurnStart OnTurnEnd OnBattleStart OnPeriodicDamage`.
  Proc effect targets: `Self` = aura owner, `Target` = the other unit of the event, `Attacker` = attacker.
* Area auras (paladin auras, totems, Consecration): `radius` (yards) + `radiusAura` (aura given to units in
  range while active) + `radiusAffects` (Allies/Enemies/All).

## Talents (`TalentTreeDef` / `TalentDef` / `PassiveDef`)

```jsonc
{
  "id": "tree_mage_fire", "classId": "Mage", "name": "Fire", "icon": "fire",
  "talents": [
    { "id": "mage_fire_improved_fireball", "name": "Improved Fireball", "tier": 1, "column": 1, "maxRank": 5,
      "description": "Reduces the casting time of your Fireball spell by {0.1/0.2/0.3/0.4/0.5} sec.",
      "effects": [ { "type": "AbilityMod", "abilities": ["mage_fireball"], "property": "CastTime", "value": -0.1 } ] },
    { "id": "mage_fire_pyroblast", "name": "Pyroblast", "tier": 3, "column": 1, "maxRank": 1,
      "description": "Grants the Pyroblast ability.",
      "effects": [ { "type": "GrantAbility", "ability": "mage_pyroblast" } ] }
  ]
}
```

* `tier` 1–7 (requires 5 × (tier−1) points spent in the tree), `column` 0–3, `maxRank` 1–5,
  `requires` (talent id of the arrow prerequisite, must be maxed).
* Passive `type`s:
  * `Stat`: `stat value|values pct school target` (`target: "Pet"` for Beast Mastery/Demonology pet talents).
  * `AbilityMod`: `property value|values` + filters `abilities tags schools` (any match). Properties:
    `Damage Healing Effect CritChance CritBonus Cost CostFlat Cooldown CastTime Range Radius Duration
    DurationPct Threat HitChance Charges ComboPoints Gcd`.
  * `Proc`: `proc` (ProcDef). If `values` is given, it overrides `proc.chance` per rank.
  * `GrantAbility`: `ability` (the ability must have `fromTalent: true`).
  * `Special`: `special` (document it in `specials`).
* Value per rank = `values[rank-1]` if given, else `value × rank`.

## Classes (`ClassDef`)

See Defs.cs. Must include `resource`, `gcd`, `armorTypes`, `armorUpgrade`, `weaponTypes`, `dualWieldLevel`,
`canParry`, `canBlock`, base stats/health/mana at levels 1 and 60, AP formulas, crit/dodge ratios, mana
regen values, `basicAttack`, `startingAbilities`, `startingItems`, `startingStance`, `talentTrees` (3),
`designNotes` (2–4 sentences on how the class plays, shown at character creation).

Shared basic attacks (`attack`, `auto_shot`, `shoot`), `help_up` and generic items live in
`Data/classes/shared.json`.

## Icon glyphs

`icon` fields name a glyph drawn on a school-coloured frame. Use one of:

```
sword swords axe mace hammer dagger daggers spear staff shield shield_bash bow arrow arrows multishot gun
trap paw claw fang wolf eagle hawk monkey cheetah turtle snake scorpion spider owl boar bear
fire fireball flame_wave meteor ember frost snowflake ice_shard ice_block frost_nova arcane arcane_orb
missiles portal blink sheep brain eye holy cross sun halo wings hand hands_pray heart heal_plus renew
shadow skull moon void tentacle demon imp curse fear drain soul_shard nature leaf lightning chain_lightning
storm earth rock wave water_drop totem totem_fire totem_earth totem_water totem_air wind wolf_spirit
stealth mask poison vial coin kick boot fist shout banner rage blood whirlwind charge stance_battle
stance_defensive stance_berserker armor aura seal judgement blessing lock key potion_red potion_blue food
drink star sparkle clock hourglass feather bandage crown
```

Unknown glyphs fall back to the ability's initial letter.
