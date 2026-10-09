import sys
from common import *
from rogue_abilities import ABILITIES, POISONS, POISON_TEXT, CHARGES
from rogue_auras import AURAS
from rogue_talents import TREES, BUILD

OUT = sys.argv[1]

CLASS = {
    "id": "Rogue", "name": "Rogue",
    "description": "Cunning and agile, rogues strike from the shadows with daggers and poison, building combo points "
                   "with quick strikes and spending them on devastating finishing moves.",
    "roles": ["Melee DPS"], "color": "#FFF569", "icon": "crest_rogue",
    "resource": "Energy", "gcd": 1.0,
    "armorTypes": ["Cloth", "Leather"], "armorUpgrade": {"level": 40, "type": "None"},
    "weaponTypes": ["Dagger", "FistWeapon", "OneHandMace", "OneHandSword", "Bow", "Crossbow", "Gun", "Thrown"],
    "dualWieldLevel": 1, "canParry": True, "canBlock": False,
    "baseStatsLevel1": {"strength": 21, "agility": 23, "stamina": 21, "intellect": 20, "spirit": 21},
    "baseStatsLevel60": {"strength": 80, "agility": 130, "stamina": 75, "intellect": 35, "spirit": 52},
    "baseHealthLevel1": 25, "baseHealthLevel60": 1523, "baseManaLevel1": 0, "baseManaLevel60": 0,
    "meleeAp": "StrengthPlusAgility",
    "agilityPerMeleeCritAt60": 29, "agilityPerDodgeAt60": 14.5, "intellectPerSpellCritAt60": 60,
    "baseMeleeCrit": 5, "baseSpellCrit": 0, "baseDodge": 5, "manaRegenBase": 0, "manaRegenPerSpirit": 0,
    "basicAttack": "attack",
    "startingAbilities": ["attack", "rogue_sinister_strike", "rogue_eviscerate", "rogue_stealth", "rogue_throw",
                          "rogue_shoot", "rogue_shadowed_presence", "rogue_dual_wield"],
    "startingItems": ["rogue_starter_dagger", "rogue_starter_dagger_offhand", "rogue_starter_vest", "rogue_starter_pants", "rogue_starter_shoes",
                      "rogue_starter_throwing_knives"],
    "startingStance": "",
    "talentTrees": [t["id"] for t in TREES],
    "defaultBuild": BUILD,
    "sprite": "char_rogue", "portrait": "portrait_rogue",
    "designNotes": "Rogues run on Energy (+60 each turn, max 100) and a 1-second global cooldown, so a turn is two or "
                   "three quick strikes: builders such as Sinister Strike and Backstab add combo points to one target, "
                   "and finishers like Eviscerate, Slice and Dice, Rupture and Kidney Shot spend them all. Open from "
                   "Stealth with Cheap Shot, Garrote or Ambush to win a surprise round, coat your blades with poisons, "
                   "and lean on Gouge, Sap, Blind, Evasion and Vanish to control or escape a fight; rogues generate 29% "
                   "less threat but wear only leather.",
}

def armor(id_, name, icon, equip, armor_val, desc, price):
    return {"id": id_, "name": name, "icon": icon, "description": desc, "kind": "Armor", "quality": "Common",
            "itemLevel": 1, "equip": equip, "armorType": "Leather", "armor": armor_val, "price": price}

ITEMS = [
    {"id": "rogue_starter_dagger", "name": "Worn Dagger", "icon": "dagger",
     "description": "A plain, well-balanced blade. Every footpad's first friend.", "kind": "Weapon",
     "quality": "Common", "itemLevel": 2, "equip": "OneHand", "weaponType": "Dagger",
     "minDamage": 1, "maxDamage": 3, "speed": 1.6, "price": 35},
    {"id": "rogue_starter_dagger_offhand", "name": "Worn Parrying Dagger", "icon": "dagger",
     "description": "A twin to your first dagger, weighted for the off hand. Every rogue fights with a blade in each hand.",
     "kind": "Weapon", "quality": "Common", "itemLevel": 2, "equip": "OffHand", "weaponType": "Dagger",
     "minDamage": 1, "maxDamage": 3, "speed": 1.6, "price": 35},
    armor("rogue_starter_vest", "Footpad's Vest", "armor", "Chest", 3,
          "Dark, close-fitting leather that doesn't creak.", 6),
    armor("rogue_starter_pants", "Footpad's Pants", "armor", "Legs", 2,
          "Soft leather trousers with more pockets than seams.", 5),
    armor("rogue_starter_shoes", "Footpad's Shoes", "boot", "Feet", 1, "Felt-soled shoes for quiet feet.", 4),
    {"id": "rogue_starter_throwing_knives", "name": "Small Throwing Knives", "icon": "dagger",
     "description": "A bandolier of small, balanced knives. They always seem to find their way back.",
     "kind": "Weapon", "quality": "Common", "itemLevel": 3, "equip": "Ranged", "weaponType": "Thrown",
     "minDamage": 2, "maxDamage": 4, "speed": 2.0, "price": 20},
    {"id": "rogue_flash_powder", "name": "Flash Powder", "icon": "sparkle",
     "description": "A pinch of blinding powder. Reagent for Vanish.", "kind": "Reagent", "quality": "Common",
     "itemLevel": 22, "stack": 20, "price": 25, "classes": ["Rogue"]},
    {"id": "rogue_blinding_powder", "name": "Blinding Powder", "icon": "sparkle",
     "description": "Ground fadeleaf that stings the eyes. Reagent for Blind.", "kind": "Reagent", "quality": "Common",
     "itemLevel": 34, "stack": 20, "price": 50, "classes": ["Rogue"]},
]
for key, name, lvl, price in POISONS:
    ITEMS.append({"id": f"rogue_{key}_poison_vial", "name": name, "icon": "vial",
                  "description": f"Use: coats your weapons for 30 min ({CHARGES[key]} charges). {POISON_TEXT[key]}",
                  "kind": "Consumable", "quality": "Common", "itemLevel": lvl, "requiredLevel": lvl,
                  "use": f"rogue_apply_{key}_poison", "consumable": True, "stack": 20, "price": price,
                  "classes": ["Rogue"]})

def sp(id_, used_by, behaviour):
    return {"id": id_, "usedBy": used_by, "behaviour": behaviour}

SPECIALS = [
    sp("RoguePerComboByRank", "rogue_eviscerate, rogue_rupture (abilities)",
       "Ability special (ModifyDamage), also applied to the periodic ticks of auras the ability applied (tick casts "
       "carry the aura's Rank/EffLevel/LearnLevel/ComboPoints). WoW finishers gain more damage PER COMBO POINT at "
       "higher ranks, which perCombo alone cannot express. For every Damage effect of the ability (Eviscerate's "
       "direct damage, Rupture's tickEffects) add amount x (effLevel - learnLevel) x comboPoints to the base "
       "magnitude, where 'amount' is that effect's amount field (per-combo-point growth per level: Eviscerate 2.582 "
       "-> 5 per point at rank 1, ~147 at rank 8; Rupture 0.3 per tick -> 2 per point per tick at rank 1, 14 at "
       "rank 6). Applied before Damage/Effect AbilityMods, crits and armor. Eviscerate's attack-power part (7% AP per "
       "point) is the generic EffectDef.apCoefPerCombo, added by Battle.EffectDamage, not by this special."),
    sp("RogueExposeArmor", "rogue_expose_armor (aura)",
       "Aura special (OnAuraApplied): after the aura resolves its mod values (Armor -80/-145/-210/-275/-340 by rank, "
       "x Improved Expose Armor EffectMult), multiply ModValues[0] by the aura's ComboPoints (the points spent by the "
       "finisher): 5 points at rank 5 = -1700 armor (-2550 with 2/2 Improved Expose Armor). Does not stack with "
       "Sunder Armor in WoW: when the target has warrior_sunder_armor, keep only the larger total armor reduction "
       "(remove the weaker one)."),
    sp("RogueKidneyShot", "rogue_kidney_shot (aura), rogue_assassination_improved_kidney_shot (talent passive)",
       "Aura special (OnAuraApplied). Duration: the data gives 1 s base + 1 s per combo point (rank 2 values, 2-6 s); "
       "at rank 1 subtract 1 s (1-5 s). Improved Kidney Shot: if the caster has the talent at rank r, set the aura's "
       "ModValues[0] (DamageTaken, pct) to 3 x r so the stunned target takes +3/6/9% damage from all sources while "
       "the stun lasts. The talent's own Special passive does nothing else."),
    sp("RogueVanish", "rogue_vanish (ability)",
       "Ability special. CheckUse: requires one rogue_flash_powder in the party inventory. Resolve (before the ability's "
       "effects): consume one Flash Powder; remove every aura on the rogue imposing Root or a negative MoveSpeed mod "
       "(snares, Daze; same rule as RogueRemoveSnares); set the rogue's threat to 0 on every enemy table and make "
       "enemies that were targeting it pick a new target; cancel its auto attack and combo-point target. Then the "
       "effects apply rogue_restealth_aura (the Stealth state with a flat -30% MoveSpeed, i.e. the max-rank Stealth "
       "penalty as a documented approximation; allowed in combat, unlike the Stealth ability) and rogue_vanish_aura (10 "
       "s; while it lasts the rogue cannot be detected even within 3 m and cannot be targeted by enemies). Rank 2 only "
       "differs in WoW stealth level (no extra rule). Openers used from Vanish stealth in an ongoing battle do NOT grant "
       "a surprise round."),
    sp("RogueBlind", "rogue_blind (ability)",
       "Ability special. CheckUse: requires one rogue_blinding_powder in the party inventory (reason 'Requires "
       "Blinding Powder'). On cast (even if the target resists) consume one Blinding Powder."),
    sp("RogueDistracted", "rogue_distract (aura)",
       "Aura special on enemies hit by Distract (10 s, broken by damage). While active the unit faces the "
       "distraction point, cannot detect stealthed units (no 3 m detection) and counts as facing away from every "
       "enemy that is not between it and the point (Backstab/Ambush/Garrote behindTarget checks pass, it cannot "
       "parry or block those attacks). Out of combat it stops patrolling for the duration. It does not skip turns."),
    sp("RoguePickLock", "rogue_pick_lock (effect)",
       "Effect special used from the exploration UI on a locked chest/door (ChestDef.lockCheck, skill "
       "SleightOfHand). After the 5 s cast the rogue rolls d20 + the normal SleightOfHand modifier + floor(level/5) "
       "(lockpicking skill) against the lock DC; natural 20 always succeeds. Success opens the lock (same outcome as "
       "passing the check); failure can be retried after another cast. Not usable in combat. A chest that does not "
       "need a check simply opens."),
    sp("RogueRelentlessStrikes", "rogue_assassination_relentless_strikes (talent passive)",
       "Talent passive: after any finisher (cost.consumesComboPoints) resolves, roll 20% x combo points spent "
       "(20..100%); on success the rogue gains 25 Energy (capped at max energy)."),
    sp("RogueMurder", "rogue_assassination_murder (talent passive)",
       "Talent passive (ModifyOutgoingDamage): all damage the rogue deals to targets of creature type Humanoid, "
       "Giant, Beast or Dragonkin is increased by 1% x rank (1/2%)."),
    sp("RogueImprovedPoisons", "rogue_assassination_improved_poisons (talent passive)",
       "Talent passive: adds 2 x rank percentage points (2..10) to the proc chance of the rogue's poison coatings "
       "(auras tagged WeaponCoating: Instant 20% -> 30%, Deadly/Crippling/Wound 30% -> 40%, Mind-numbing 20% -> "
       "30%)."),
    sp("RogueVigor", "rogue_assassination_vigor (talent passive)",
       "Talent passive: maximum Energy +10 (100 -> 110). Turn regeneration is unchanged (+60)."),
    sp("RogueCamouflage", "rogue_subtlety_camouflage (talent passive)",
       "Talent passive: while the rogue has an aura with the Stealth state, MoveSpeed +3% x rank (pct, offsets the "
       "stealth movement penalty: -50/-40/-35/-30% by Stealth rank, -30% when Vanish or Improved Sap restealths via "
       "rogue_restealth_aura). The Stealth cooldown reduction is a separate AbilityMod."),
    sp("RogueMasterOfDeception", "rogue_subtlety_master_of_deception (talent passive)",
       "Talent passive: enemies' stealth detection radius against this rogue (base 3 m out of combat, see Design.md "
       "Stealth) is reduced by 10% x rank (to 1.5 m at 5/5); in-combat detection checks against the rogue take the "
       "same reduction."),
    sp("RogueRemoveSnares", "rogue_combat_improved_sprint (proc effect)",
       "Effect special (target Self): removes every aura on the target that imposes the Root state or has a negative "
       "MoveSpeed stat mod (snares, Daze, Hamstring, Frost Nova...). Stun and other control effects are not removed."),
    sp("RogueSleightOfHand", "rogue_subtlety_sleight_of_hand (talent passive)",
       "Talent passive: melee and ranged attacks against the rogue have their critical strike chance reduced by "
       "1% x rank (applied in the attacker's crit roll, not below 0). The Feint part is an AbilityMod Threat."),
    sp("RogueHeightenedSenses", "rogue_subtlety_heightened_senses (talent passive)",
       "Talent passive (IncomingMissChance hook): spells (incl. wand shots) and ranged attacks (Auto Shot, ranged weapon "
       "abilities, thrown) aimed at the rogue get +value x rank percentage points (2/4) of miss chance, added to the "
       "attacker's miss chance before the 99% hit cap (spells: the resist roll; physical ranged: the miss part of the "
       "ranged table). Melee attacks are not affected. The Stealth detection part is a separate Stat passive."),
    sp("RogueSerratedBlades", "rogue_subtlety_serrated_blades (talent passive)",
       "Talent passive: the rogue's physical attacks ignore armor equal to values[rank-1] x rogue level (2.67/5.43/8 "
       "per level: 160/326/480 at level 60), i.e. a level-scaled ArmorPenetration stat. The Rupture damage bonus is a "
       "separate AbilityMod."),
]

GENERIC_FROM_WARRIOR = ["RankDurations", "SweepingHits", "WeaponTypeTalent", "OffHandDamage", "PhysicalRangedShot"]

abilities = finish_abilities(ABILITIES, "Rogue")
check_build(TREES, BUILD)
used, unused = write_bundle(OUT, "Rogue class data (WoW Classic 1.12). Generated; generic specials RankDurations, "
                            "SweepingHits, WeaponTypeTalent, OffHandDamage and PhysicalRangedShot are documented in "
                            "warrior.json. Lanternvale change: rogues dual wield from level 1 (dualWieldLevel 1; Dual "
                            "Wield is a starting passive, not sold by the trainer, so it never gates: "
                            "EquipmentRules.ProficiencyPassive skips starting passives and old saves learn it on load); "
                            "the starter off-hand dagger is a real Off Hand weapon (equip OffHand). Lanternvale change "
                            "(finishers, turn-based pacing): combo points belong to the rogue for the whole battle (they "
                            "carry over when the target dies or the rogue switches target; cleared when spent, by Vanish "
                            "and at battle end), and Eviscerate adds 7% attack power per combo point (apCoefPerCombo "
                            "0.07, the WotLK value; 1.12 had none) with rank 2 at level 6 instead of 8, because only "
                            "about one finisher lands per fight while builders and white hits scale with weapon and AP. "
                            "Ranks 1 and 8 keep WoW's base numbers.", [CLASS], abilities, AURAS, TREES, ITEMS, SPECIALS,
                            extra_specials=GENERIC_FROM_WARRIOR)
print("rogue: abilities", len(abilities), "auras", len(AURAS), "items", len(ITEMS),
      "talents", [len(t["talents"]) for t in TREES], "specials", sorted(used))
print("unused documented:", sorted(unused))
