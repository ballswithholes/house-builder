# Lanternvale — Game Design & Rules Specification

Lanternvale is a party-based, turn-based CRPG in the spirit of Baldur's Gate 3, played on hand-painted
2.5D dioramas (orthographic camera, Ghibli-inspired watercolour environments) with Final Fantasy X–inspired
heroes. The eight classes play like their **World of Warcraft Classic** counterparts: the same abilities,
ranks, talent trees, weapons/armour, roles and resources.

This document is the contract between the rules engine, the data files and the Unity presentation layer.

---

## 1. Pillars

1. **WoW Classic class fantasy, turn-based.** Rage builds from hitting and being hit, energy ticks up and
   combo points feed finishers, mana is governed by Spirit and the five-second rule, Warlocks juggle soul
   shards, Hunters fight alongside a pet with a dead zone at close range, Shamans drop one totem per element.
2. **BG3 party tactics.** Party of up to 4 (+ pets/totems), initiative, movement in metres, positioning,
   flanking (behind-target abilities), surprise from stealth, skill checks in dialogue with a d20.
3. **Threat matters.** Enemies use a WoW threat table. Tanks taunt and hold aggro; healers draw threat.
4. **Cosy, readable presentation.** Soft 2D lights, parallax layers, clean silhouettes, clear UI.

## 2. Time model — "six-second turns"

WoW is real-time; Lanternvale slices it into **rounds of 6 seconds**. Every unit takes one turn per round.

* Each turn a unit has **6.0 s of Time** and a separate **Movement** budget (default 9 m).
* Using an ability costs Time:
  * `time: "Gcd"` (default): `max(castTime, gcd)` where gcd is the class GCD (1.5 s; Rogues 1.0 s) unless
    `gcdOverride` is set.
  * `time: "OffGcd"`: costs only `castTime` (usually 0). Stances, Bloodrage, Cold Blood, Presence of Mind, Judgement…
* An ability may be **started with any positive Time left**. If its time cost exceeds what is left:
  * **Instant**: it resolves now; the overflow becomes **time debt** that reduces next turn's Time.
  * **Cast time / channel**: the cast is **pending** and resolves at the start of the unit's next turn,
    which then has `6 - overflow` seconds. The unit's turn ends immediately (it is casting). A pending cast
    can be **interrupted** (Kick, Pummel, Counterspell, Earth Shock, Spell Lock…) or cancelled by stun,
    silence (for spells), fear, polymorph, incapacitate or death. This is how enemy "big casts" are telegraphed.
* Channelled abilities (`channeled: true`, `channelTicks`) resolve ticks proportionally to the time spent
  this turn; remaining ticks resolve at the start of the next turn if not interrupted.
* Movement is separate from Time. A unit with a pending cast cannot move. Moving does not interrupt casts that
  already resolved. Roots stop movement; snares reduce it (`MoveSpeed` stat, pct).
* **Auto attacks run in parallel** with abilities, exactly like WoW swing timers. At the **end of a unit's
  turn**, if auto attack is on and its attack target is in reach (melee reach, or ranged range outside the
  dead zone for Auto Shot), the unit swings: `swingTimer += 6 * hasteMultiplier`, and for each full weapon
  `speed` in the timer one swing happens (main hand, and off hand separately when dual wielding). If not in
  reach the timer is capped at one ready swing. Heroic Strike / Cleave / Raptor Strike (`nextSwing: true`)
  replace the next main-hand swing.
  Casters "Shoot" a wand by using the Shoot ability (costs wand speed seconds of Time).
* **Durations and cooldowns are in seconds.** At the start of a unit's turn, 6 s elapse for that unit's
  auras (and auras it suffers) and its cooldowns. Periodic auras tick `6 / tickInterval` times per round
  (fractional ticks carry over). Out of combat, time runs in real time (1 s = 1 s): cooldowns recover, buffs
  expire, resources regenerate.
* Resource regeneration at the start of each turn (6 s worth):
  * Energy: +20 per 2 s = **+60** (modified by `EnergyRegen` pct). Max 100.
  * Rage: no passive regen; decays 1 per second out of combat.
  * Mana: Spirit regen per 2 s tick = `manaRegenBase + Spirit * manaRegenPerSpirit` (class data), ×3 per turn.
    **Five-second rule:** if the unit spent mana during its previous turn, only `SpiritRegenWhileCasting`% of
    Spirit regen applies (MP5 always applies, ×6/5 per turn).
  * Focus (hunter pets): +24 per 4 s ⇒ +36 per turn (modified by the pet's `EnergyRegen` pct, e.g. Bestial
    Discipline +10/20%; out of combat +6/s × `EnergyRegen`). Max 100.
  * Health: no in-combat regen except HP5/effects. Out of combat Spirit-based regen (generous for pacing).

## 3. Units, stats and formulas (WoW Classic)

* Levels 1–60. XP table = WoW Classic, gains multiplied by `config.xpRate` (default 4).
* Base primary stats interpolate linearly between `baseStatsLevel1` and `baseStatsLevel60` (ClassDef).
  Companions add `statBonus`.
* **Health** = baseHealth(level) + min(20, Sta) + max(0, Sta − 20) × 10. **Mana** = baseMana(level) + min(20, Int) + max(0, Int − 20) × 15.
* **Attack power** (melee): Warrior/Paladin/Shaman `Str×2 + level×3 − 20`; Rogue/Hunter `Str + Agi + level×2 − 20`;
  casters `Str − 10`. **Ranged AP**: Hunter `Agi×2 + level×2 − 10`; Warrior/Rogue `Agi + level − 10`.
* Weapon swing damage = weapon roll + `AP / 14 × weapon speed`. Off hand deals 50%.
* **Crit**: melee `baseMeleeCrit + Agi / (agilityPerMeleeCritAt60 × level / 60)`; spell
  `baseSpellCrit + Int / (intellectPerSpellCritAt60 × level / 60)` (denominators floor at 1). Melee/ranged
  crits ×2, spell crits ×1.5 (+`CritDamage`).
* **Avoidance**: dodge `baseDodge + Agi / (agilityPerDodgeAt60 × level/60)`; parry 5% (canParry, frontal);
  block 5% + `Block` (shield, frontal), blocks reduce damage by `BlockValue` (+ Str/20). Attacks from behind
  cannot be parried or blocked. Spells cannot be dodged/parried/blocked.
* **Hit**: melee miss 5% (+1% per level the target is above, dual-wield white swings +19%), −`MeleeHit`.
  Spell miss 4% (+1/+2/+11 for targets 1/2/3+ levels higher), −`SpellHit`. Max hit chance 99%.
* **Armor** mitigation = `armor / (armor + 400 + 85 × attackerLevel)`, capped at 75%. Physical only.
* **Resistance** average mitigation = `resist / (5 × attackerLevel) × 0.75` capped at 75%.
* Spell damage = base + `coef × SpellDamage(school)`. Heal = base + `coef × HealingPower`.
  Ability effect magnitudes: `rand(min,max) + perLevel × (effLevel − learnLevel) + perCombo × CP` where
  effLevel = level of the highest rank known (rankLevels), or caster level if `scaleWithLevel` / creature.
* **Rage**: conversion `c = 0.0091107836 L² + 3.225598133 L + 4.2652911`. Dealing white damage `d`:
  `+7.5 × d / c × 1.75` (turn compression factor), taking damage `+2.5 × d / c`. Max 100. Out of combat
  rage decays. Stance swap keeps up to 0 rage (Tactical Mastery: up to 5 per rank).
* **Threat**: 1 per damage, healing 0.5 per point split among engaged enemies, mana/rage gains 0.5/5.
  Modifiers: Defensive Stance ×1.3, Battle/Berserker ×0.8, Rogue ×0.71 (class passive aura),
  Righteous Fury Holy ×1.6. AI targets the highest threat it can reach; it switches only when another unit
  exceeds 110% (melee) / 130% (ranged) of the current target's threat. Taunt: forces the taunted enemy to
  target the taunter on its next turn and sets taunter threat = highest threat.
* **Combo points** (Rogue): stored on the rogue for one target; switching target loses them. Max 5.
  Builders with `generatesComboPoint` add 1 on hit (+`ComboPoints` mods). Finishers (`cost.consumesComboPoints`)
  scale with `perCombo` / `durationPerCombo` and spend all points.
* **Soul shards** (Warlock): an inventory item `soul_shard` (max 32 in bags). Drain Soul killing blow creates one.
  Summons other than Imp, Soulstone, Healthstone and Shadowburn consume one (`cost` cannot express this —
  use a `CreateItem`/`special: "ConsumeSoulShard"` requirement per data docs).
* **Pets**: Hunter pets (Focus) and Warlock demons (Mana) are party units with their own turn right after
  their owner, fully controllable, with AI fallback. One pet per owner. **Totems** (one per element) are
  stationary units with no turn: their periodic auras/abilities act at the end of the owner's turn.
  **Hunter traps** (one at a time, a `Trap` totem) may be laid on any turn in battle as well as before a pull.
  This is a deliberate deviation from 1.12, where traps needed the hunter out of combat (before the pull or
  after a successful Feign Death): `Battle.InCombat` is one flag for the whole fight, so Feign Death does not
  drop the hunter out of combat (see the `HunterTrap` / `HunterFeignDeath` specials in `hunter.json`).
* **Stealth**: out of combat, stealthed units are not seen by encounters unless within 3 m. Starting combat
  from stealth (attacking with an Opener) grants a **surprise round**: enemies lose their first turn.
* **Death**: party members at 0 HP are **Downed**. An adjacent ally can spend 1.5 s to *Help* them up at 1 HP
  (BG3). Resurrection spells work in and out of combat (WoW classic: only out of combat for most). If the
  whole party is downed the game is over (load last save).
* **Resting**: Long rest at an inn/camp restores everything and advances time. Out of combat, characters
  regenerate quickly; Food/Drink items restore faster (WoW style).

## 4. Combat flow

1. An encounter triggers (party walks into aggro radius, dialogue outcome, or the player attacks).
2. All combatants roll initiative: `d20 + (Agi − 20) / 10` (ties: higher Agi). Surprised units skip round 1.
3. Rounds proceed in initiative order. Pets act right after their owner. On a unit's turn:
   start-of-turn processing → actions (abilities, movement, item use, end turn) → end-of-turn auto attacks
   → end-of-turn totem actions.
4. Combat ends when no hostile unit remains active. XP and loot are granted; downed allies get up at 1 HP.

## 5. Progression

* **Trainers** in towns teach new abilities and ranks for gold (WoW style). Companions are trained too.
* **Talents**: 1 point per level from level 10 (51 at 60). 3 trees per class, 7 tiers, 5 points per tier,
  prerequisite arrows, WoW Classic talents. Respec at the trainer for gold (1g, then 5g, 10g …).
* **Equipment**: 17 slots. Armour proficiency per class (Warrior/Paladin Mail→Plate at 40, Hunter/Shaman
  Leather→Mail at 40). Weapon proficiencies per WoW Classic. Item qualities Poor→Legendary; random
  "of the Bear/Eagle/Monkey…" suffix greens.

## 6. Exploration & story

* Click to move; the party follows the leader in formation; A* on a navigation grid (0.5 m cells).
* Interactables: NPCs (dialogue, vendors, trainers, innkeepers), chests (some locked: Sleight of Hand check),
  signs, shrines, area transitions.
* **Dialogue** with branching choices, class-specific options `[PALADIN]`, and **d20 skill checks**:
  `d20 + modifier ≥ DC`. Modifier = `(stat − 20) / 10` (rounded down, from the relevant primary stat) + 2 if
  the class is proficient (Warrior/Paladin: Athletics, Intimidation; Rogue: Stealth, SleightOfHand,
  Acrobatics; Hunter: Survival, Nature; Mage: Arcana, History; Priest: Religion, Insight; Warlock: Arcana,
  Intimidation; Shaman: Nature, Insight). Natural 20 always succeeds, natural 1 always fails.
* **Quests** with stages and objectives; companion approval.

## 7. Content of the vertical slice

* **Lanternvale Village** (hub): Elder, innkeeper, merchants, a trainer for each class, recruitable companions.
* **Whisperwood** (forest): wolves, boars, mischievous Mosslings, bandits; side quests.
* **The Old Lantern Shrine** (dungeon): blighted spirits, the boss *The Hollow Warden*.

Story: the spirit-lanterns that protect the valley are going dark one by one. A creeping *Hollow* — a
grey blight that empties spirits — spreads from the old shrine. The party must rekindle the lanterns.

## 8. Architecture

```
Assets/Lanternvale/
  Scripts/Core      pure C# (asmdef noEngineReferences): Json, Data, Rules (engine), Util
  Scripts/Game      Unity layer: bootstrap, world/rendering, input, camera, UI (IMGUI), audio, presentation
  Scripts/Editor    editor tooling: scene setup, URP 2D setup, art import settings, data validation
  Resources/Data    JSON DataBundles (classes, content)
  Resources/Art     PNGs + art_manifest.json (placeholders generated by Tools/artgen; replace freely)
Tools/check.sh       compile + validate + test without Unity
Docs/                design, schema, API and art prompt documents
```

The rules engine emits a stream of `CombatEvent`s that the Unity layer animates. All randomness goes through
`Rng` so battles are reproducible in tests.
