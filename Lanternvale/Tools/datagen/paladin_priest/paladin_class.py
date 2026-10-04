# Paladin: ClassDef, starter items, specials documentation.
from paladin_talents import DEFAULT_BUILD

CLASS = {
    "id": "Paladin", "name": "Paladin",
    "description": "A holy warrior in plate and tabard who wields hammer, seal and blessing. Paladins shield their "
                   "allies, heal in a pinch, and judge their foes with radiant light.",
    "roles": ["Tank", "Healer", "Melee DPS", "Support"],
    "color": "#F58CBA", "icon": "crest_paladin",
    "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth", "Leather", "Mail"], "armorUpgrade": {"level": 40, "type": "Plate"},
    "weaponTypes": ["OneHandMace", "TwoHandMace", "OneHandSword", "TwoHandSword", "OneHandAxe", "TwoHandAxe",
                    "Polearm", "Shield"],
    "dualWieldLevel": 0, "canParry": True, "canBlock": True,
    "_statsNote": "Human paladin, naked. Level 1: 22/20/22/20/22 -> 58 health, 80 mana (WoW Classic).",
    "baseStatsLevel1": {"strength": 22, "agility": 20, "stamina": 22, "intellect": 20, "spirit": 22},
    "baseStatsLevel60": {"strength": 105, "agility": 69, "stamina": 102, "intellect": 75, "spirit": 82},
    "baseHealthLevel1": 18, "baseHealthLevel60": 1381,
    "baseManaLevel1": 60, "baseManaLevel60": 1512,
    "meleeAp": "StrengthTimesTwo", "rangedAp": "Strength",
    "agilityPerMeleeCritAt60": 20, "intellectPerSpellCritAt60": 54, "agilityPerDodgeAt60": 20,
    "baseMeleeCrit": 5, "baseSpellCrit": 3.3, "baseDodge": 0.7,
    "manaRegenBase": 15, "manaRegenPerSpirit": 0.2,
    "basicAttack": "attack",
    "startingAbilities": ["attack", "paladin_holy_light", "paladin_seal_of_righteousness", "paladin_devotion_aura"],
    "startingItems": ["paladin_starter_hammer", "paladin_starter_vest", "paladin_starter_leggings",
                      "paladin_starter_boots"],
    "talentTrees": ["tree_paladin_holy", "tree_paladin_protection", "tree_paladin_retribution"],
    "defaultBuild": DEFAULT_BUILD,
    "sprite": "char_paladin", "portrait": "portrait_paladin",
    "designNotes": "Keep a Seal up, swing your weapon and spend it with an off-GCD Judgement (10 sec cooldown), then "
                   "reseal. One Blessing per Paladin per ally and one Aura per Paladin keep the whole party stronger. "
                   "Mana is precious: Holy Light and Flash of Light heal well, Lay on Hands and Divine Shield save the "
                   "day, and Righteous Fury plus Holy Shield turn the Paladin into a tank.",
}


def item(id_, name, icon, desc, **kw):
    d = {"id": id_, "name": name, "icon": icon, "description": desc}
    d.update(kw)
    return d


ITEMS = [
    item("paladin_starter_hammer", "Battleworn Hammer", "hammer",
         "A squire's two-handed hammer, nicked from years of practice in the chapel yard.",
         kind="Weapon", quality="Common", itemLevel=2, requiredLevel=1, equip="TwoHand", weaponType="TwoHandMace",
         minDamage=5, maxDamage=9, speed=2.9, price=45, classes=["Paladin"]),
    item("paladin_starter_vest", "Squire's Chain Vest", "armor",
         "Riveted rings over a sun-stitched tabard.",
         kind="Armor", quality="Common", itemLevel=2, requiredLevel=1, equip="Chest", armorType="Mail", armor=21,
         price=30, classes=["Paladin"]),
    item("paladin_starter_leggings", "Squire's Leggings", "armor", "Sturdy leather leggings, freshly oiled.",
         kind="Armor", quality="Common", itemLevel=2, requiredLevel=1, equip="Legs", armorType="Leather", armor=9,
         price=20, classes=["Paladin"]),
    item("paladin_starter_boots", "Squire's Boots", "boot", "Good boots for long marches.",
         kind="Armor", quality="Common", itemLevel=1, requiredLevel=1, equip="Feet", armorType="Leather", armor=5,
         price=10, classes=["Paladin"]),
]

SPECIALS = [
    {"id": "PaladinJudgement", "usedBy": "paladin_judgement",
     "behaviour": "Effect of Judgement (target: enemy within 10 yd; off-GCD; requires a caster aura tagged Seal). Find "
                  "the caster's active aura in exclusiveGroup 'paladin_seal'. Map it to its judgement ability by id: "
                  "paladin_seal_of_righteousness -> paladin_judgement_of_righteousness, paladin_seal_of_the_crusader "
                  "-> paladin_judgement_of_the_crusader, paladin_seal_of_justice -> paladin_judgement_of_justice, "
                  "paladin_seal_of_light -> paladin_judgement_of_light, paladin_seal_of_wisdom -> "
                  "paladin_judgement_of_wisdom, paladin_seal_of_command -> paladin_judgement_of_command (i.e. replace "
                  "'seal_of_' by 'judgement_of_'). Remove the seal aura, then execute the hidden judgement ability on "
                  "the Judgement target for free, as if cast by the paladin: it uses the RANK of the seal (effLevel = "
                  "highest rankLevels entry of the seal ability the paladin knows; the judgement abilities share the "
                  "seal's learnLevel/rankLevels), rolls a Holy spell hit (a miss still consumes the seal) and can "
                  "crit as a spell. AbilityMods that target the judgement ability id apply. Judgement debuffs are in "
                  "exclusiveGroup 'paladin_judgement' (exclusivePerCaster) so a new Judgement replaces the "
                  "paladin's previous one on that target. Threat: the damage/effect threat of the triggered ability."},
    {"id": "PaladinDoubleVsStunned", "usedBy": "paladin_judgement_of_command",
     "behaviour": "Modifier on a Damage effect: compute the damage normally, then multiply by 2 if the target "
                  "currently has an aura with the Stun state (from any source)."},
    {"id": "PaladinJudgementOfTheCrusader", "usedBy": "paladin_judgement_of_the_crusader_debuff",
     "behaviour": "While the bearer has this debuff, every Holy-school Damage effect dealt to it by ANY attacker "
                  "(spell, proc, seal, tick) is computed as if the attacker had extra Holy spell damage of X, i.e. "
                  "+X x (that effect's coef; use 1.0 for effects whose coef is 0 and that are not weapon-based; "
                  "WeaponDamage effects converted to Holy, like Seal of Command, use 0.2). X by rank of the paladin's "
                  "Seal of the Crusader: 23/35/58/92/127/140 (ranks 1-6, learned at 6/12/22/32/42/52), multiplied by "
                  "(1 + AbilityMod Effect % on paladin_judgement_of_the_crusader, e.g. Improved Seal of the "
                  "Crusader +15%)."},
    {"id": "PaladinJudgementOfJustice", "usedBy": "paladin_judgement_of_justice_debuff",
     "behaviour": "While active the bearer cannot flee: AI 'Coward' fleeing and voluntary retreat are disabled and "
                  "positive MoveSpeed modifiers are ignored (movement cannot exceed its normal speed). Fear effects "
                  "still work."},
    {"id": "PaladinBlessingOfLight", "usedBy": "paladin_blessing_of_light, paladin_greater_blessing_of_light",
     "behaviour": "Heals from paladin_holy_light received by the bearer gain +H and heals from paladin_flash_of_light "
                  "gain +F (added to the base before crit, by any caster). Blessing of Light ranks 1-3 (levels "
                  "40/50/60): H = 210/300/400, F = 60/85/115. Greater Blessing of Light: H = 400, F = 115. Scale by "
                  "AbilityMod Effect % on the blessing ability."},
    {"id": "PaladinBlessingOfSanctuary", "usedBy": "paladin_blessing_of_sanctuary, paladin_greater_blessing_of_sanctuary",
     "behaviour": "Each incoming damage instance against the bearer (any school, after armor/resistance, before "
                  "absorbs) is reduced by a flat R (not below 0). Blessing of Sanctuary ranks 1-4 (levels 30/40/50/60): "
                  "R = 10/14/19/24. Greater Blessing of Sanctuary: R = 24. The OnBlock Holy damage to the attacker is "
                  "an ordinary proc on the aura."},
    {"id": "PaladinBlessingOfFreedom", "usedBy": "paladin_blessing_of_freedom",
     "behaviour": "On apply, remove the bearer's Root auras and auras with negative MoveSpeed mods. While active, "
                  "ignore new Root states and negative MoveSpeed mods on the bearer (auras still land; they simply "
                  "have no movement effect). Duration 10 sec + AbilityMod Duration (Guardian's Favor +3/6 sec)."},
    {"id": "PaladinBlessingOfSacrifice", "usedBy": "paladin_blessing_of_sacrifice",
     "behaviour": "Each damage instance the bearer takes is reduced by up to T and the caster paladin takes that "
                  "amount instead (unmitigated, cannot be redirected again; if the paladin is dead/out of the "
                  "encounter the blessing is removed). T by rank (levels 46/54): 30/40. Cannot target self."},
    {"id": "PaladinForbearanceCheck",
     "usedBy": "paladin_blessing_of_protection, paladin_divine_protection, paladin_divine_shield",
     "behaviour": "Cast-validation hook only (all listed effects then resolve normally): the ability cannot be used "
                  "on a target (for Divine Protection/Shield: the caster) that has the aura paladin_forbearance. The "
                  "UI greys the target out and the AI never selects it. The ability's own effects apply "
                  "paladin_forbearance (60 sec)."},
    {"id": "PaladinLayOnHands", "usedBy": "paladin_lay_on_hands",
     "behaviour": "Read the caster's CURRENT mana M before the cost (cost.consumeAll then sets the caster's mana to 0). "
                  "Heal the target for the caster's maximum health (not affected by healing power, can't crit, "
                  "threat as a normal heal). Rank 2 (level 30) additionally gives the target 250 mana, rank 3 (level "
                  "50) 550 mana. Requires M > 0 to cast (WoW: LoH needs some mana)."},
    {"id": "PaladinDivineIntervention", "usedBy": "paladin_divine_intervention",
     "behaviour": "Target: another living party member within 30 yd. Apply paladin_divine_intervention (3 min: "
                  "Banish + Invulnerable, no actions, enemies drop all threat on them and cannot target them). Then "
                  "the paladin dies immediately (Downed state skipped: becomes dead and must be resurrected; no "
                  "Help-up). If the encounter ends while the protected unit is the last one standing the party "
                  "loses as normal. The aura is removed early when combat ends."},
    {"id": "PaladinHolyShock", "usedBy": "paladin_holy_shock",
     "behaviour": "Friend-or-foe: if the target is hostile, resolve only the Damage effect (effects[0]); if friendly "
                  "(including self), resolve only the Heal effect (effects[1]). Holy school, can crit; Divine Favor "
                  "and Illumination apply to the heal."},
    {"id": "PaladinConcentrationAura", "usedBy": "paladin_concentration_aura_effect",
     "behaviour": "Casting pushback: when a unit with a pending cast or channel takes damage, the engine adds 0.5 sec "
                  "(WoW 1.0 s pushback, halved for 6-second rounds) to the remaining cast time (channels lose that "
                  "much channel time instead), at most twice per cast. While this aura is on the unit, pushback is "
                  "reduced by 35% (+5/10/15% with Improved Concentration Aura, read from AbilityMod Effect on "
                  "paladin_concentration_aura), and Improved Concentration Aura also gives the same 5/10/15% chance "
                  "to resist Silence and Interrupt effects. If the engine does not implement pushback, only the "
                  "resist part applies."},
    {"id": "PaladinPushbackResist", "usedBy": "paladin_holy_spiritual_focus",
     "behaviour": "Talent passive: values[rank-1] = % chance that a pending paladin_holy_light or "
                  "paladin_flash_of_light cast ignores casting pushback (see PaladinConcentrationAura for the "
                  "pushback rule)."},
    {"id": "PaladinUnyieldingFaith", "usedBy": "paladin_holy_unyielding_faith",
     "behaviour": "Talent passive: values[rank-1] = additional % chance to resist auras with the Fear or Confuse "
                  "state (disorient) when they would be applied to the paladin."},
    {"id": "PaladinIllumination", "usedBy": "paladin_holy_illumination",
     "behaviour": "Proc effect after a healing critical effect of Holy Light, Flash of Light or Holy Shock (heal "
                  "branch only): the paladin gains mana equal to the BASE mana cost of the rank used (cost.amount + "
                  "cost.perLevel x (rankLevel - learnLevel), before talent reductions)."},
    {"id": "PaladinReckoning", "usedBy": "paladin_protection_reckoning",
     "behaviour": "Grant the paladin one stored extra attack (max 4 stored). At the end of the paladin's next turn, "
                  "if auto attack is on and the target is in reach, perform one additional main-hand swing per "
                  "stored extra attack (consuming them), in addition to the normal swing timer swings. Stored extra "
                  "attacks are lost when combat ends."},
    {"id": "PaladinWeaponSpecialization",
     "usedBy": "paladin_protection_one_handed_weapon_specialization, paladin_retribution_two_handed_weapon_specialization",
     "behaviour": "Talent passive: values[rank-1] = % increase to all damage the paladin deals while wielding a "
                  "one-handed (One-Handed Weapon Specialization) or two-handed (Two-Handed Weapon Specialization) "
                  "melee weapon in the main hand. Applies to auto attacks, weapon-based abilities and Holy damage "
                  "from seals/judgements (WoW 1.12 behaviour)."},
    {"id": "PaladinEyeForAnEye", "usedBy": "paladin_retribution_eye_for_an_eye",
     "behaviour": "On a spell (non-Physical) critical hit taken by the paladin, deal Holy damage to the attacker "
                  "equal to 15% x talent rank of the damage taken, capped at 50% of the paladin's maximum health. "
                  "Cannot crit, no coefficient, cannot be resisted."},
]
