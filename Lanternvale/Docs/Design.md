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
2. **BG3 party tactics.** Party of up to 5 (+ pets/totems; raids up to 10), initiative, movement in metres, positioning,
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
  **Opening swing:** the first main-hand melee swing of a battle (and a ready off-hand one) lands the moment the
  unit engages in its own turn — when it starts its melee auto attack on a hostile in reach, queues a next-swing
  ability, or after a melee strike (Rend, Sinister Strike) has resolved — instead of at the end of the turn. It is
  taken from that turn's swing time (the timers go below zero), so the number of swings does not change, only when the
  first one lands; a queued Heroic Strike replaces it. This is how a warrior, who starts every fight at 0 rage, gets
  the rage for a strike in the turn he engages. It applies to every melee unit, enemies included.
  A queued next-swing ability **holds its cost**: other abilities paying with the same resource may only spend the
  rest ("Not enough rage (10): 15 is held for Heroic Strike"). If its swing still cannot pay (a stance swap, Execute's
  drain) it fails visibly (`CastFailed`, "Heroic Strike failed") and a white swing lands instead.
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
  Ability effect magnitudes: `rand(min,max) + perLevel × (effLevel − learnLevel) + perCombo × CP` (+ `apCoefPerCombo ×
  AP × CP` on a finisher's Damage effect) where
  effLevel = level of the highest rank known (rankLevels), or caster level if `scaleWithLevel` / creature.
* **Rage**: conversion `c = 0.0091107836 L² + 3.225598133 L + 4.2652911`. Dealing white damage `d`:
  `+7.5 × d / c × 1.75` (turn compression factor), taking damage `+2.5 × d / c`. Max 100. Out of combat
  rage decays. Stance swap keeps up to 0 rage (Tactical Mastery: up to 5 per rank). Every warrior attack costs rage,
  so a refusal for rage always says what to do ("Not enough rage (15). Attack an enemy to build rage."), and in combat
  the player's strike on an enemy attacks it first when the rage is missing (CombatFlow.md §3). Charge (8–25 yd) only
  opens a fight: from the battle formation the click steps back out of its minimum range first.
* **Threat**: 1 per damage, healing 0.5 per point split among engaged enemies, mana/rage gains 0.5/5.
  Modifiers: Defensive Stance ×1.3, Battle/Berserker ×0.8, Rogue ×0.71 (class passive aura),
  Righteous Fury Holy ×1.6. AI targets the highest threat it can reach; it switches only when another unit
  exceeds 110% (melee) / 130% (ranged) of the current target's threat. Taunt: forces the taunted enemy to
  target the taunter on its next turn and sets taunter threat = highest threat.
* **Combo points** (Rogue): stored on the rogue, max 5. **Lanternvale change** (turn-based pacing, modern WoW): they
  belong to the rogue for the whole battle, so they carry over when the target dies or the rogue builds on or finishes
  another enemy (1.12 lost them on a target switch); cleared when spent, by Vanish and at battle end.
  Builders with `generatesComboPoint` add 1 on hit (+`ComboPoints` mods). Finishers (`cost.consumesComboPoints`)
  scale with `perCombo` / `durationPerCombo` (+ `apCoefPerCombo` × AP per point) and spend all points on a hit (an
  avoided finisher keeps them). **Eviscerate** adds 7% AP per point (`apCoefPerCombo` 0.07, the WotLK value) and gets
  rank 2 at level 6: only about one finisher lands per fight while builders and white hits scale with weapon and AP,
  so 3 points now hit for ~2-3x a Sinister Strike and 5 points ~3-5x (rank-1 base numbers stay WoW's 6-10 ... 26-30).
* **Soul shards** (Warlock): an inventory item `soul_shard` (max 32 in bags). A Drain Soul killing blow creates one
  (`special: "WarlockDrainSoulShard"`), and so does a target dying under Shadowburn's hidden 5 s debuff (aura
  `special: "WarlockShadowburnShard"`). Summon Voidwalker/Succubus/Felhunter, Soul Fire, Shadowburn and every
  Healthstone/Soulstone/Spellstone/Firestone creation consume one (the Imp is free). `cost` cannot express a reagent, so such an ability sets
  `special: "WarlockConsumeSoulShard"` (unusable without a shard; one is taken when the cast resolves).
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
  whole party is downed the game is over (load last save) — except in a raid: a wipe sends the party, healed, back
  to the raid's entrance zone (no lockouts; try again).
* **Resting**: Long rest at an inn/camp restores everything and advances time. Out of combat, characters
  regenerate quickly; Food/Drink items restore faster (WoW style).

## 4. Combat flow

1. An encounter triggers (party walks into aggro radius, dialogue outcome, or the player attacks). The party steps
   into a **battle formation** facing the enemies: tanks in front (about 2.5 m short of the nearest enemy), melee
   just behind, ranged, then healers at the back, pets beside their owners. Not when a stealthed attacker or an
   opener (Charge, Cheap Shot…) starts the fight — there the positions are the plan — and not at training dummies.
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
  Intimidation; Shaman: Nature, Insight). The party member with the best modifier rolls. Natural 20 always
  succeeds, natural 1 always fails. A rogue picking a lock adds level / 5.
* **Quests** with stages and objectives; companion approval.
* **Party and raids.** The active party holds up to 5 characters (`config.partySize`); other recruits wait at camp.
  Ordinary encounters grow tougher with a bigger party: enemy health × `1 + 0.2 · max(0, n − 4)` for n party
  characters (×1.2 at 5). A **raid** map (`MapDef.raidSize`, 10) is entered with a party the player picks from
  everyone recruited (a picker opens at the raid's door; companions auto-play by default). Raid creatures are
  tuned in data (Elite trash, Boss bosses, no health scale). Leaving the raid brings the previous party, leader and
  auto-play back; a wipe sends everyone home healed. In big fights DPS companions assist the tank nearest to them,
  and two companion healers never heal the same target in one round.

## 7. Content

The first slice (levels 1–12) and the expansion *The Ember Road* (levels 12–33; contract and as-built notes in
`Docs/Expansion.md`, links and coordinates in its §1). Enemies scale to the party's level (`scaleToParty` +
`levelOffset`), clamped to their map's band (`levelFloor` / `levelCap`); XP per kill and quest follows
`config.xpRateByLevel` (×4 to level 12, rising to ×9 at 30) at the content's level (the creature's, the quest's),
never above the character's, so arriving early or late does not change what a zone pays. Every map lists its band on the Map panel and in the
journal; NPCs show WoW-style quest markers (§6 "Quests", `Docs/WorldAPI.md` §5).

**The valley (levels 1–12, deepened by the expansion).**
* **Lanternvale Village** (hub, 90×44): Elder Maru, the inn, merchants, a weaponsmith and an armourer, four trainers
  (each teaches two classes), companions Kael, Aldric and Pip. The northern band adds the west road to Amberfield, the
  millpond, the Lantern Meadow, Pipp Orchard, Hollyhock Farm and Lantern Hill, with 3 side quests (rootlings in the
  orchard, singing roots, lamp oil and Duskmane scouts).
* **Whisperwood** (forest, 100×46): wolves, boars, mischievous Mosslings, bandits, spiders, owls, blighted spirits;
  the old forest to the north with its brook, fords and the Heron Watch; companions Rook, Seren, Lys; 2 more side quests.
* **The Old Lantern Shrine** (70×40): hollowed spirits, Keeper Ishiro, the boss *The Hollow Warden*; the terraced climb
  north (Lantern Steps, Pilgrims' Rest, the Keeper's Lodge, the High Terrace) and 1 side quest; companions Torvan, Morwen.
* Quests: the main quest *The Lanterns Go Dark*, five original side quests and six new ones.

**Hidden dungeons** (one under every map; harder: +2 levels, elite packs, a named boss with an Epic chance and a
guaranteed Rare, 2–3 chests, no resting). Each is revealed by a Perception check near its entrance (rolled once, by the
best party member) or by an NPC's hint or an inspected prop; the entrance then appears with a "hidden passage" banner.

| Dungeon | Levels | Under | Boss | Quest |
|---|---|---|---|---|
| The Root Hollows | 11–13 | Lanternvale (Kusu's roots) | The Rootwarden | *The Heart Under the Roots* |
| Mossdeep Grotto | 12–14 | Whisperwood (Mossy Hollow) | King Umbercap, the Moss King | *The Glow Under the Moss* |
| The Lantern Catacombs | 13–15 | the Shrine (High Terrace) | Hazama, the Lantern Lich | *The Keepers' Rest* |
| The Barrow of King Aldwin | 18–20 | Amberfield (the King's Ring) | King Aldwin the Unquiet | *The King Under the Hill* |
| The Drowned Vault | 24–26 | Mirefen (the Sunken Statue) | The Tidewitch | *The Undertow* |
| The Frozen Sanctum | 30–32 | Skyreach (the Frozen Falls) | The Rimeheart | *The Long Watch* |

**New zones (levels 12–30).** The road runs west from Lanternvale: Amberfield → Brightwater, which branches to Mirefen
and Skyreach.
* **Amberfield Downs** (12–18, 130×56): golden downs, farmsteads, windmills and standing stones; Duskmane gnolls
  stealing lantern oil (their warcamp and Warchief Skarra), Mudpaw tunnelers (fight or make peace), hawks, the
  Ditchwater Gang; 12 quests including *The Ember Road*; companion **Bruna** (Warrior, tank) at Haybright Farm.
* **Brightwater** (hub 12–30, 84×44): a river trade town with an inn, general goods, a weaponsmith, an armourer,
  reagents and potions, four trainers, Archivist Penhallow (the main-story hub), a ferry and 6 town quests; companions
  **Ysolde** (Paladin, tank) and **Liora** (Priest, healer).
* **Mirefen** (18–24, 120×56): misty fen of boardwalks and stilt villages; mirelings, crocolisks, bog ghouls, fen wisps
  and Mother Mire's coven (Auntie Gall); 11 quests including *The Drowned Lanterns*; companion **Nanami** (Shaman,
  healer) in Lowlantern; the portal to the Hollow Heart.
* **Skyreach Peaks** (24–30, 120×60): snow, pines and the mountain hut village of Cairnhollow; yetis, frost wolves,
  harpies, ogres, ice and ash elementals, drakes and the Dragonsworn vanguard (Vanguard-Marshal Kaedric); 10 quests
  including *Ash on the Wind*, which reveals the Roost.

**Raids** (raid party of up to 10, picked at the door from everyone recruited; Elite trash and Boss bosses with
telegraphed and uninterruptible raid-wide casts, adds and enrages; a wipe sends everyone home healed, no lockouts).
Each boss drops class set pieces (tier 1 / tier 2: 8 classes × head, shoulders, chest, hands, legs; bonuses at 2, 4 and
5 pieces) and raid epics for the party's classes — two of each in a raid of ten — gold and a Rare; the final bosses can
drop legendaries (one per party).

| Raid | Levels | Entrance | Bosses (in order) | Legendaries |
|---|---|---|---|---|
| The Hollow Heart | 21–23 | Mirefen, the Moon-Gate portal | Thornmaw the Rootbound; the Weeping Twins (Sorrow and Solace); Mother Mire; The Hollow Heart | Kindlewood, Last Staff of the Heart Lantern; Solace, the Twins' Lullaby |
| Ashwyrm's Roost | 31–33 | Skyreach, the Roost Gate (after *Ash on the Wind*) | Frostclaw the Matriarch; the Cinder Drakes (Emberjaw and Ashtongue); Highlord Varkas; Vyrmathra the Ashwyrm | Embersong, Fang of Vyrmathra; Dawnstring, the Last Light of Skyreach; Vyrmathra's Last Scale |

**Off Hand weapons.** Besides One-Hand weapons (either hand), the game has *Off Hand* weapons: daggers, one-handed
swords, fist weapons, maces and axes with `equip: "OffHand"` that go only in the off hand and need dual wield exactly
like a One-Hand weapon there (the wielder must also use the weapon type). Their DPS follows the one-hand `WeaponDps`
rule for their item level and quality (Common about ×0.6, Uncommon ×0.72, Rare ×0.79, Epic ×0.86); their stats use the
OffHand slot budget (0.56), a little richer than a One-Hand weapon's (0.45) because they are less flexible. Where they
are (20 in all, besides the rogue's starting Worn Parrying Dagger; `TestsContentOffHandWeapons`):

| Band | Vendor | Loot |
|---|---|---|
| 1–12 | Garrow (Lanternvale): Second-Best Paring Knife (dagger, level 3), Lefty's Shortsword (sword, level 6) | Cocooned Knuckle-Claw (fist, spiders), Cutpurse's Second Opinion (dagger, bandits), Rusk's Small Change (sword, Rusk) |
| 12–18 | Dunstan (Brightwater): Hollowell Main-Gauche (dagger, 16) | Duskmane Tally-Knife (dagger, gnolls), Mudpaw Left Claw (fist, Burrowmaster Grubb); Mossdeep: Royal Toadstool Tenderiser (Rare mace, King Umbercap) |
| 18–24 | Dunstan: Floodgate Knuckles (fist, 22) | Mireling Reed-Cutter (dagger, mirelings), Snapjaw Hatchet (axe, crocolisks); the Barrow: Huscarl's Bearded Seax (Rare dagger, King Aldwin) |
| 24–30 | Dunstan: Left-Bank Sabre (sword, 28) | Harpy-Quill Sabre (sword, harpies), Gorrum's Toothpick (dagger, Gorrum Two-Belly); the Drowned Vault: Undertow Cutlass (Rare, the Tidewitch); the Frozen Sanctum: Rimeheart Knuckle (Rare fist, the Rimeheart) |
| raids | — | Solace's Edge (Epic sword, the Weeping Twins), Frostclaw's Dewclaw (Epic fist, Frostclaw) |

**Companions** (12, one per class in the slice and four more in the new zones): Kael (Warrior), Aldric (Paladin), Pip
(Rogue) in Lanternvale; Rook (Hunter), Seren (Priest), Lys (Mage) in Whisperwood; Torvan (Shaman), Morwen (Warlock) at
the Shrine; Bruna (Warrior, Amberfield), Ysolde (Paladin, Brightwater), Liora (Priest, Brightwater), Nanami (Shaman,
Mirefen). The active party holds 5; the rest wait at camp.

Story: the spirit-lanterns that protect the valley are going dark one by one. A creeping *Hollow* — a
grey blight that empties spirits — spreads from the old shrine. The party must rekindle the lanterns. Then the Heart
Lantern shows a vision: the Hollow was a seed carried on ash from the north, where the Ashwyrm **Vyrmathra** drowses
beneath Skyreach. *The Ember Road* leads through Amberfield to Brightwater's archivist, *The Drowned Lanterns* through
Mirefen to the Hollow Heart, and *Ash on the Wind* through Skyreach to the Roost and Vyrmathra.

## 8. Architecture

```
Assets/Lanternvale/
  Scripts/Core      pure C# (asmdef noEngineReferences): Util, Json, Data, Rules (engine), World (nav, flags,
                    dialogue, quests, map state), Session (GameSession: the API the Unity layer uses, saves)
  Scripts/Game      Unity layer: bootstrap, art, rendering & lighting, input, world/units/FX/audio presentation,
                    game flow (exploration, combat presenter, saves), UI (IMGUI)
  Scripts/Editor    editor tooling: scene setup, URP 2D setup, art import settings, data validation
  Resources/Data    JSON DataBundles (classes, content)
  Resources/Art     PNGs + art_manifest.json (placeholders generated by Tools/artgen; replace freely)
Tools/check.sh       compile + validate + test without Unity
Docs/                design, schema, API and art prompt documents
```

The rules engine emits a stream of `CombatEvent`s that the Unity layer animates; `GameSession` raises
`SessionEvent`s for everything else. All randomness goes through `Rng` so battles are reproducible in tests.
See `Architecture.md` for the layer diagram.
