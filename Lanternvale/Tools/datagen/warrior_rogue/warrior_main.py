import sys
from common import *
from warrior_abilities import ABILITIES
from warrior_auras import AURAS
from warrior_talents import TREES, BUILD

OUT = sys.argv[1]

CLASS = {
    "id": "Warrior", "name": "Warrior",
    "description": "Masters of weapons and armour who thrive in the thick of battle, warriors draw strength from the "
                   "fury of combat itself: every blow struck and suffered feeds their Rage.",
    "roles": ["Tank", "Melee DPS"], "color": "#C79C6E", "icon": "crest_warrior",
    "resource": "Rage", "gcd": 1.5,
    "armorTypes": ["Cloth", "Leather", "Mail"], "armorUpgrade": {"level": 40, "type": "Plate"},
    "weaponTypes": ["Dagger", "FistWeapon", "OneHandAxe", "OneHandMace", "OneHandSword", "Polearm", "Staff",
                    "TwoHandAxe", "TwoHandMace", "TwoHandSword", "Bow", "Crossbow", "Gun", "Thrown", "Shield"],
    "dualWieldLevel": 20, "canParry": True, "canBlock": True,
    "baseStatsLevel1": {"strength": 23, "agility": 20, "stamina": 22, "intellect": 20, "spirit": 21},
    "baseStatsLevel60": {"strength": 120, "agility": 80, "stamina": 110, "intellect": 30, "spirit": 47},
    "baseHealthLevel1": 20, "baseHealthLevel60": 1689, "baseManaLevel1": 0, "baseManaLevel60": 0,
    "meleeAp": "StrengthTimesTwo",
    "agilityPerMeleeCritAt60": 20, "agilityPerDodgeAt60": 20, "intellectPerSpellCritAt60": 60,
    "baseMeleeCrit": 5, "baseSpellCrit": 0, "baseDodge": 5, "manaRegenBase": 0, "manaRegenPerSpirit": 0,
    "basicAttack": "attack",
    "startingAbilities": ["attack", "warrior_battle_stance", "warrior_heroic_strike", "warrior_battle_shout",
                          "warrior_shoot", "warrior_throw"],
    "startingItems": ["warrior_starter_shortsword", "warrior_starter_shirt", "warrior_starter_pants",
                      "warrior_starter_boots"],
    "startingStance": "warrior_battle_stance_aura",
    "talentTrees": [t["id"] for t in TREES],
    "defaultBuild": BUILD,
    "sprite": "char_warrior", "portrait": "portrait_warrior",
    "designNotes": "Warriors have no mana: Rage builds from the white swings you land at the end of each turn and "
                   "from the blows you take, and decays out of combat. Stances, Bloodrage and the big cooldowns cost no "
                   "Time, and Heroic Strike/Cleave replace your next end-of-turn swing, so a turn is a stance dance "
                   "plus one or two GCD attacks: Charge and Overpower in Battle Stance, Taunt, Sunder and Revenge as "
                   "the party's tank in Defensive Stance, Whirlwind, Intercept and Pummel in Berserker Stance. Arms "
                   "and Fury reward big two-handers or dual wielding; Protection turns a shield into a wall.",
}

def armor(id_, name, icon, equip, armor_type, armor_val, desc, price):
    return {"id": id_, "name": name, "icon": icon, "description": desc, "kind": "Armor", "quality": "Common",
            "itemLevel": 1, "equip": equip, "armorType": armor_type, "armor": armor_val, "price": price}

ITEMS = [
    {"id": "warrior_starter_shortsword", "name": "Worn Shortsword", "icon": "sword",
     "description": "A recruit's blade, notched from a hundred training-yard drills.", "kind": "Weapon",
     "quality": "Common", "itemLevel": 2, "equip": "OneHand", "weaponType": "OneHandSword",
     "minDamage": 1, "maxDamage": 3, "speed": 1.9, "price": 35},
    armor("warrior_starter_shirt", "Recruit's Shirt", "armor", "Chest", "Cloth", 0,
          "A plain linen shirt issued to every new recruit. Offers no protection to speak of.", 5),
    armor("warrior_starter_pants", "Recruit's Pants", "armor", "Legs", "Cloth", 1,
          "Sturdy trousers with patched knees.", 5),
    armor("warrior_starter_boots", "Recruit's Boots", "boot", "Feet", "Cloth", 1,
          "Hobnailed boots that have marched many a mile.", 5),
]

def sp(id_, used_by, behaviour):
    return {"id": id_, "usedBy": used_by, "behaviour": behaviour}

SPECIALS = [
    sp("WarriorStanceSwap", "warrior_battle_stance, warrior_defensive_stance, warrior_berserker_stance (effect), "
       "warrior_arms_tactical_mastery (talent passive)",
       "Effect special (type Special, target Self) that runs before the stance's ApplyAura. If the warrior already has "
       "the target stance aura the ability is unusable (CheckUse). Otherwise its rage becomes min(currentRage, keep) "
       "where keep = 5 x rank of Tactical Mastery (that talent's Special passive, value 5 per rank: 0-25; 0 without "
       "the talent). Then the stance aura is applied; its exclusiveGroup 'warrior_stance' removes the previous stance. "
       "Stance abilities share cooldownGroup 'warrior_stance' (1 s), i.e. at most one stance change per turn. "
       "Abilities whose requires.casterAuras list stance auras are unusable in other stances. The Tactical Mastery "
       "passive has no other effect."),
    sp("WarriorCharge", "warrior_charge (ability)",
       "Ability special (CheckUse). WoW Charge cannot be used while the warrior is in combat. In a battle the warrior "
       "counts as 'out of combat' until it has dealt damage, taken damage or used any ability against an enemy in this "
       "battle (normally only its first action of the fight); afterwards Charge is unusable for the rest of the battle "
       "(Intercept is the in-combat version). The warrior also counts as in combat while it has the warrior_bloodrage "
       "aura (Bloodrage: 'considered in combat for the duration'), so Charge is unusable until Bloodrage expires; the "
       "opener is Charge first, then Bloodrage. Outside battles Charge is used like an opener and starts the encounter. "
       "Other requirements (Battle Stance, 8-25 yd range, cooldown) are the normal data checks."),
    sp("WarriorRetaliation", "warrior_retaliation (aura)",
       "Aura special (AllowsAuraProc). The aura's OnStruck proc (an instant 100% weapon damage counterattack on the "
       "attacker that uses one of the 30 charges) does not fire, and uses no charge, when the attacker is behind the "
       "warrior: the same facing test as requires.behindTarget, seen from the bearer (attacker.IsBehind(warrior), i.e. "
       "the attacker stands outside the warrior's frontal arc). OnStruck is raised only by melee hits, so spells and "
       "ranged attacks never cause retaliation."),
    sp("WarriorOverpower", "warrior_overpower (ability)",
       "Ability special: the Overpower attack cannot be dodged, parried or blocked (roll only miss/hit/crit on the "
       "melee table). Using it consumes the TargetDodged reactive window (requires.reactive)."),
    sp("WarriorExecute", "warrior_execute (ability)",
       "Ability special (ModifyDamage). Cost 15 rage (after Improved Execute CostFlat) is paid normally; cost.consumeAll "
       "then drains ALL remaining rage into cast.ExtraResource. Bonus damage = ExtraResource x 3 x rankNumber "
       "(rankNumber 1..5 = rank used, i.e. 3/6/9/12/15 damage per extra rage point), added to the Damage effect's "
       "base (125 at rank 1 ... 600 at rank 5) before crit/armor. Resolved as a normal melee special attack "
       "(miss/dodge/parry/block, crit x2 plus Impale). The rage is lost even if the attack misses."),
    sp("WarriorDefiance", "warrior_protection_defiance (talent passive)",
       "Talent passive: while the warrior has warrior_defensive_stance_aura, ThreatGenerated +3% per rank (pct stat "
       "added to the stance's +30%: x1.30 -> up to x1.45). No effect in other stances."),
    sp("WarriorImprovedCleave", "warrior_fury_improved_cleave (talent passive)",
       "Talent passive: the flat bonus of warrior_cleave's WeaponDamage effect (its min/max + perLevel part, not the "
       "weapon roll or AP) is multiplied by (1 + 0.40 x rank): +40/80/120%."),
    sp("WarriorIronWill", "warrior_protection_iron_will (talent passive)",
       "Talent passive: whenever a hostile aura imposing the Stun state (or a charm/mind-control effect) would be "
       "applied to the warrior, roll 3% x rank (3/6/9/12/15); on success the aura is resisted (emit a Resist event)."),
    sp("WarriorBerserkerRage", "warrior_berserker_rage (aura)",
       "Aura special. On apply: removes the bearer's auras whose states include Fear, Incapacitate or Sleep (Sap, "
       "Gouge, Intimidating Shout, Psychic Scream...). While active: hostile auras imposing Fear, Incapacitate or Sleep "
       "are not applied (emit Immune), and rage generated by taking damage is doubled (2 x the normal 2.5 x d / c)."),
    sp("ImmunityFromTags", "warrior_recklessness, warrior_death_wish (auras)",
       "Aura special: while the aura is active its bearer is immune to the unit states named by the aura's tags: "
       "'ImmuneFear' -> Fear, 'ImmuneIncapacitate' -> Incapacitate and Sleep, 'ImmuneStun' -> Stun, 'ImmuneRoot' -> "
       "Root. Hostile auras imposing such a state are not applied (emit Immune), and existing ones are removed when "
       "this aura is applied."),
    sp("RankDurations", "warrior_thunder_clap, rogue_sap (auras)",
       "Aura special for auras whose duration grows with the rank of the applying ability. The aura's tags contain "
       "one entry 'Durations:a/b/c/...' (seconds per rank). On apply, the base duration becomes the value for the "
       "aura's Rank (clamped to the list), then the applying effect's durationPerCombo x combo points and the source "
       "ability's AbilityMod Duration / DurationPct are applied as usual (same formula as AbilityRules.AuraDuration). "
       "The aura's own 'duration' field is the rank-1 fallback."),
    sp("SweepingHits", "warrior_sweeping_strikes, rogue_blade_flurry (auras)",
       "Aura special: while active, every melee hit by the bearer on its target (auto attacks and single-target melee "
       "abilities; NOT area abilities such as Whirlwind/Cleave, not periodic damage, not the copies themselves) also "
       "deals the same final damage (after the primary target's mitigation, no new roll, no procs) to one other "
       "enemy within 8 yards of the primary target (nearest first). If the aura has charges, each copied hit "
       "consumes one charge and the aura ends at 0 charges."),
    sp("WeaponTypeTalent", "warrior/rogue weapon specialization talents (talent passives)",
       "Talent passive restricted to the weapon types listed in its 'tags' (WeaponType names, plus 'OneHanded' = any "
       "one-handed melee weapon and 'TwoHanded' = any two-handed melee weapon). (a) If 'stat' is set: the stat mod "
       "(value x rank, or values[rank-1]; pct flag) applies only to attacks made with a weapon of a listed type, judged "
       "per hand (an off-hand dagger benefits from Dagger Specialization even with a main-hand sword); for "
       "non-weapon attacks the main hand decides. (b) If 'proc' is set: it behaves exactly like a Proc passive (chance "
       "= values[rank-1]) but only triggers on melee hits made with a weapon of a listed type. An extra attack granted "
       "this way (Sword Specialization: WeaponDamage 100% = one extra swing with the same hand; normal hit table, can "
       "crit) cannot itself trigger the same proc."),
    sp("OffHandDamage", "warrior_fury_dual_wield_specialization, rogue_combat_dual_wield_specialization (talent passives)",
       "Talent passive (OffHandMultiplier hook): damage of off-hand weapon hits (auto attacks and off-hand "
       "WeaponDamage) is multiplied by (1 + value x rank / 100), on top of the normal 50% off-hand penalty "
       "(warrior value 5, rogue value 10)."),
    sp("PhysicalRangedShot", "warrior_shoot, warrior_throw, rogue_shoot, rogue_throw (abilities)",
       "Ability special for warrior/rogue ranged attacks (not auto-repeat). TimeCost = equipped ranged weapon speed "
       "/ (1 + RangedHaste%/100) instead of the GCD (overflow becomes time debt like other instants). CheckUse: "
       "abilities tagged 'Thrown' need a ranged weapon of type Thrown; abilities tagged 'Shoot' need a Bow, Gun or "
       "Crossbow. Resolves the ability's single ranged WeaponDamage hit: ranged attack power (Agi + level - 10 for "
       "Warrior and Rogue), Physical, ranged hit table (miss/dodge, no parry/block), RangedCrit, crit x2. No ammo is "
       "consumed. Must NOT start Auto Shot."),
]

classes = [CLASS]
abilities = finish_abilities(ABILITIES, "Warrior")
check_build(TREES, BUILD)
used, unused = write_bundle(OUT, "Warrior class data (WoW Classic 1.12). Generated; see Docs/DataSchema.md.",
                            classes, abilities, AURAS, TREES, ITEMS, SPECIALS,
                            extra_specials=())
print("warrior: abilities", len(abilities), "auras", len(AURAS), "items", len(ITEMS),
      "talents", [len(t["talents"]) for t in TREES], "specials used", sorted(used))
print("documented but unused here (used by rogue):", sorted(unused))
