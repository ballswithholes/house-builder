# Lanternvale — Data Schema Guide

All game data lives in `Assets/Lanternvale/Resources/Data/**.json`. Every file is a **DataBundle** — an
object with any of these arrays: `classes`, `abilities`, `auras`, `talentTrees`, `items`, `itemSuffixes`,
`lootTables`, `creatures`, `npcs`, `companions`, `dialogues`, `quests`, `maps`, `specials`, `itemSets`, and an
optional `config` object. The loader merges all files. **The C# definitions in
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
* `rankCastTimes` (optional, one value per rank): per-rank cast times for WoW's faster low ranks (Fireball Rank 1
  1.5 s); empty = `castTime` for every rank. Used when a lower rank is cast (downranking).
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
  `Telegraph` (engine rule): a cast-time ability that always becomes a pending cast in combat — it resolves at the start
  of the caster's next turn even if it would fit, so the party can interrupt/stun/silence it (boss big casts).
  `Uninterruptible` (engine rule, cast-time or channelled abilities only — the validator checks): interrupts, silences
  and crowd control do not cancel the cast (an interrupt logs "immune" and locks no school); control that takes the
  caster's whole turn only delays it. The companion AI does not spend interrupts or stuns on it. For raid-wide boss casts
  the healers must heal through; keep heals and the casts whose text asks for an interrupt interruptible. Say so in
  the description ("It cannot be interrupted.").
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

## Expansion fields ("The Ember Road", `Docs/Expansion.md` §2)

Every field below is optional; its default keeps the 1–12 slice's behaviour. The validator rules of
`Docs/Expansion.md` §2.9 (in `DataValidator.cs`) check them; `Tools/harness/CoreTests/TestsExpansionContract.cs`
shows one mistake per rule.

### Quests, NPCs, regions (markers and the journal)

| Def.field | Type, default | Meaning |
|---|---|---|
| `QuestDef.minLevel` | int, 0 | Main character level needed to be offered the quest (grey `!` below it). When > 1, every dialogue choice or node whose outcomes `StartQuest` it must carry a `Level` condition ≥ minLevel (on the choice or its node). |
| `QuestDef.zone` | map id, "" | Groups the quest in the journal. Must be a known map. |
| `QuestStageDef.turnIn` | npc/companion id, "" | The NPC the quest marker points at for this stage ("" = inferred). Also excuses a `Flag` stage whose flag no data sets (set by code). |
| `NpcDef.shortName` | string, "" | Map panel label ("" = `name`). |
| `NpcDef.scale` | float, 1 | Size of the NPC's model as a multiplier of its natural height (a gnoll pup on the adult's model: 0.6). Must be in [0.3, 3]. The game's unit view and preview3d apply it. |
| `RegionDef.name` | string, "" | Map panel cluster label. |

Quest rules: `giver` is a known npc or companion; something starts every quest (a `StartQuest`/`SetQuestStage`
outcome in a dialogue or a quest's `onComplete`); a `Talk` objective's target has a dialogue (an npc's `dialogue` or a
companion's `recruitDialogue`; the objective completes when that dialogue ends); a `Flag` objective's flag is set by
data (`SetFlag`, region `enterFlag`/`checkFlag`, an encounter's done flag, `Recruit` → `recruited_<id>`) or the stage
has a `turnIn`.

### Hidden things and discovery

| Def.field | Type, default | Meaning |
|---|---|---|
| `TransitionDef.hidden` | bool, false | Invisible and unusable (`MapRuntime.TransitionAt` skips it) until `Flags.Test(revealFlag)`. Needs a `revealFlag`. |
| `TransitionDef.revealFlag` | flag expression, "" | Reveals a hidden transition. |
| `TransitionDef.marker` | `""` (auto) \| `none` \| `cave` \| `door` \| `stairs` \| `portal` | How the exit is drawn. |
| `PropDef.requireFlag` / `hideFlag` | flag expressions, "" | Prop shown only while `requireFlag` holds and `hideFlag` does not (`MapRuntime.IsPropVisible`). |
| `PropDef.dialogue` | dialogue id, "" | Started when the prop is clicked; the owner is the prop's `interact` id. |
| `RegionDef.check` | `CheckDef`, null | A passive check rolled once per save on the first entry while `requireFlag` holds, by the best party member. Only `skill` and `dc` are used. Needs a `checkFlag`. |
| `RegionDef.checkFlag` | flag, "" | Set when the check succeeds. |
| `RegionDef.successText` / `failText` | string, "" | Toasts for the roll. |
| `RegionDef.requireFlag` | flag expression, "" | Gate for the check. |
| `SkillCheck.Perception` | enum value | Spirit; Hunters and Rogues are proficient. |

Ids are unique per map across prop `interact`, chest, transition and region ids, and **encounter ids are unique
across all maps** (the done flag `enc_<id>` is global).

### Map look and terrain

| Def.field | Type, default | Meaning |
|---|---|---|
| `MapDef.biome` | "", `meadow` `village` `forest` `shrine` `highlands` `fen` `peaks` `cave` `ice_cave` `crypt` `hollow_heart` `roost` | Terrain/backdrop style ("" = the old keyword fallback on `ground`/layers). |
| `MapDef.environment` | "" (= `outdoor`), `cave`, `crypt` | Indoor maps: no sky, sun or hills; rock or masonry walls, indoor light, near dark fog. |
| `MapDef.paths` | list of `PathDef {art = "decal_path_dirt", points (≥ 2), width = 2.2}` | Painted path polylines in any direction. |
| `MapDef.water` | list of `WaterDef {points, halfWidth = 1.5, closed, crossings: [RectDef {pos, size}], blocksMovement = true}` | Streams (≥ 2 points) or ponds (`closed`, ≥ 3 points). Crossings are walkable fords and bridges. |
| `MapDef.fill` | float 0–1, 0 | Density of procedural interior ground cover. |
| `MapDef.levelMin` / `levelMax` | int, 0 | The zone's level band (Map panel, journal). |
| `MapDef.raidSize` | int 0..`config.maxRaidSize`, 0 | > 0: a raid map; the party may hold up to raidSize characters there. A raid needs `raidReturnMap` + `raidReturnSpawn` (a known map and one of its spawns). |
| `MapDef.dungeon` | bool, false | A hidden dungeon (UI badge). |
| `AmbientDef.snow` / `ash` / `dust` / `drips` | bool, false | New ambient particle kinds. |

### Creatures and loot

| Def.field | Type, default | Meaning |
|---|---|---|
| `CreatureDef.levelFloor` / `levelCap` | int, 0 | Clamp of `scaleToParty` levels: `clamp(partyLevel + levelOffset, max(1, floor), cap > 0 ? cap : 63)`. floor ≤ cap when both are set. Explicit encounter levels and non-scaling creatures are unaffected. |
| `CreatureDef.material` | "", `plate mail leather cloth flesh fur chitin bone wood stone ether ice scale wet` | Impact sound ("" = inferred). |
| `CreatureDef.voice` | "", `beast humanoid spirit wood stone dragon frog gnoll none` | Death and vocal sounds ("" = inferred). |
| `LootEntryDef.pool` | item ids, [] | The entry picks one id from the pool. Exclusive with `item` and `random`. |
| `LootEntryDef.weights` | floats, [] | Pool weights: empty (equal) or one per pool id. |
| `LootEntryDef.skipOwned` | bool, false | Pool: skip ids the party already owns. |
| `LootEntryDef.perMembers` | int ≥ 0, 0 | > 0: one roll per `perMembers` party members (a raid of 10 with 5 rolls twice). |
| `LootEntryDef.partyUsable` | bool, false | Pool: only ids some party member can equip. |

`random: true` with `quality` Epic or Legendary is an error: author epics and legendaries and drop them from a pool.

### Items, sets and legendaries

| Def.field | Type, default | Meaning |
|---|---|---|
| `PassiveDef.description` | string, "" | Tooltip text of an equip effect / set bonus effect, used first when set. |
| `DataBundle.itemSets` | list of `ItemSetDef {id, name, items[], bonuses[]}` | Item sets. A set's `items` list is the only source of membership; an item belongs to at most one set. |
| `SetBonusDef` | `{pieces = 2, stats: [StatModDef], equipEffects: [PassiveDef], description}` | Active while ≥ `pieces` distinct set items are equipped. `pieces` strictly increasing, in [1, items]. Effect types: Stat, AbilityMod, Proc or Special (no GrantAbility). |

A set needs a name and at least 2 equipable items. `quality: Legendary` ⇒ `unique: true`. Item `equipEffects[].type`
must be Stat, AbilityMod, Proc, GrantAbility or Special.

### Config, XP and party

| Field | Default (shipped) | Meaning |
|---|---|---|
| `config.partySize` | 4 (5) | Active party size outside raids. |
| `config.maxRaidSize` | 10 | Upper bound of `MapDef.raidSize`. |
| `config.xpRateByLevel` | [] (`[{1,4},{12,4},{18,6},{24,8},{30,9},{60,9}]`) | `XpRatePoint {level, rate}` list, linearly interpolated by the receiving character's level (clamped to the end points) and applied to kill and quest XP (`Progression.XpRate`). Empty = `xpRate` for every level. |

## Items, sets and loot (behaviour)

The fields are in the tables above (`### Creatures and loot`, `### Items, sets and legendaries`); this is how the engine
uses them (`Docs/CoreAPI.md` §6).

**Equip effects** (`ItemDef.equipEffects`, `SetBonusDef.equipEffects`, both `PassiveDef`):

| `type` | Applied by | Tooltip (when `description` is empty) |
|---|---|---|
| `Stat` | the stat sheet (target `Self`) | `Equip: +5 Strength` |
| `AbilityMod` | `AbilityMods.For` (filters `abilities` / `tags` / `schools`, any match; none = every ability) | `Equip: Reduces the cost of Heroic Strike by 50%.` |
| `Proc` | `Battle.FireProcs` at rank 1 (no level scaling). Weapon procs fire only from that weapon's swings; trinket, armour and set procs on every matching trigger. Chance = `values[0]`, else `proc.chance`, or `ppm` × weapon speed / 60 | `Chance on hit:` (melee, auto-attack and ranged hits), `When struck:` (OnStruck), else `Equip: Chance on <trigger>:`, then the first effect (an aura's description, damage with chain jumps, healing, an extra attack, a triggered ability), then `(5% chance, 30 sec cooldown)` / `(2 procs per minute)` |
| `Special` | the named `Specials` handler, rank 1 | the handler name |
| `GrantAbility` | **not applied for items** (talents only); an error in set bonuses | the ability's description |

`PassiveDef.description` replaces the generated text; when it starts with its own prefix (`Equip:`, `Chance on`,
`When struck:`, `Use:`) it is the whole line, otherwise the prefix is added. The proc odds are always appended.
Tooltips also show `Item Level N` for gear.

**Sets.** List the set's items in `itemSets[].items` (the only membership source; sets may list items of other files).
A bonus is active while at least `pieces` distinct set items are worn: two copies of a set ring count once. The
tooltip shows the set name with the worn count, every piece (lit when worn by the hovered member) and every bonus as
`(n) Set: …` (green while active; `SetBonusDef.description`, else the generated stats and effects). The character
sheet lists worn sets; equipping a piece that switches a new bonus on raises a `Toast` session event with Id
`set_complete` (Id2 = set id, Amount = that bonus's pieces). Set and pool items are never handed out as veteran
starting gear.

**Loot entries.**
* `item`: that item; `random`: a generated item of `quality` at the creature's (or the party's, for chests) level +
  `itemLevelOffset` (never Epic or Legendary); `pool`: one id of the pool.
* `chance` is rolled per roll; `min..max` is the stack size.
* `perMembers` > 0 rolls the entry `ceil(members / perMembers)` times (members = the party characters in the fight;
  5 in a raid of 10 gives 2 rolls). It works for `item`, `random` and `pool` entries.
* Pools prefer ids that have not dropped yet in the same battle or chest, so one fight's bosses and a raid's rolls
  give different pieces while the pool allows.
* `skipOwned` removes ids the party owns (bags, the open loot window, anything a roster member wears, camp included)
  or that already dropped in this battle: nothing drops when every id is excluded (one-per-party legendaries: a pool
  of one with `skipOwned`, plus `unique: true`).
* `partyUsable` keeps ids some party member could equip by class, armour and weapon type (the required level is
  ignored, so gear a few levels ahead still drops).
* `weights`: one per pool id (empty = equal); 0 never drops.
* Tables that use none of `pool`, `perMembers` roll exactly as before (same RNG use), so seeded tests stay stable.

**Budget guardrail.** `TestsItemSetsAndLoot.AuthoredStatBudget_WithinBounds` requires every Uncommon+ equipable item
of item level 12+ to have authored stats (and `Stat` effects) costing between 0.8× and 3.2× `ItemGenerator.StatBudget`
for its level, quality and slot (cost per point: primary stats, resistances 1; AttackPower 0.5; SpellDamage 0.86;
HealingPower 0.45; crit, hit, dodge, parry 14; Armor 0.1; mana/health regen 2.5). Recommended authored budgets:
`0.55 × ilvl × {Uncommon 1.1, Rare 1.6, Epic 2.1, Legendary 2.8} × slot` (slot: chest/legs/head/two-hand 1, shoulder,
hands, feet, waist 0.75, wrist, neck, back, finger, trinket, off hand 0.56, one-hand 0.45, ranged 0.35).
