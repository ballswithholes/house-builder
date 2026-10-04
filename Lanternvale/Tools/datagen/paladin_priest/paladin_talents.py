# Paladin talent trees (WoW Classic 1.12) and talent-proc auras.
from common import *

AURAS = []
T = talent

HL, FOL, HS = "paladin_holy_light", "paladin_flash_of_light", "paladin_holy_shock"
MAGIC = ["Holy", "Fire", "Nature", "Frost", "Shadow", "Arcane"]


def h(n):
    return "paladin_holy_" + n


def p(n):
    return "paladin_protection_" + n


def rt(n):
    return "paladin_retribution_" + n


HOLY = {
    "id": "tree_paladin_holy", "classId": "Paladin", "name": "Holy", "icon": "holy",
    "description": "Healing light, holy spells and Seal of Righteousness.",
    "talents": [
        T(h("divine_strength"), "Divine Strength", "fist", 1, 1, 5,
          "Increases your Strength by {2/4/6/8/10}%.", [P_stat("Strength", 2, pct=True)]),
        T(h("divine_intellect"), "Divine Intellect", "brain", 1, 2, 5,
          "Increases your total Intellect by {2/4/6/8/10}%.", [P_stat("Intellect", 2, pct=True)]),
        T(h("spiritual_focus"), "Spiritual Focus", "hands_pray", 2, 1, 5,
          "Gives your Flash of Light and Holy Light spells a {14/28/42/56/70}% chance to not lose casting time when "
          "you take damage.", [P_special("PaladinPushbackResist", values=[14, 28, 42, 56, 70])]),
        T(h("improved_seal_of_righteousness"), "Improved Seal of Righteousness", "seal", 2, 2, 5,
          "Increases the damage done by your Seal of Righteousness and its Judgement by {3/6/9/12/15}%.",
          [P_mod("Effect", 3, abilities=["paladin_seal_of_righteousness"]),
           P_mod("Damage", 3, abilities=["paladin_judgement_of_righteousness"])]),
        T(h("healing_light"), "Healing Light", "heal_plus", 3, 0, 3,
          "Increases the amount healed by your Holy Light and Flash of Light spells by {4/8/12}%.",
          [P_mod("Healing", 4, abilities=[HL, FOL])]),
        T(h("consecration"), "Consecration", "sun", 3, 1, 1,
          "Consecrates the land beneath the Paladin, doing 64 Holy damage over 8 sec to enemies who enter the area.",
          [P_grant("paladin_consecration")]),
        T(h("improved_lay_on_hands"), "Improved Lay on Hands", "hands_pray", 3, 2, 2,
          "Gives the target of your Lay on Hands spell a {15/30}% bonus to their armor value from items for 2 min. "
          "In addition, the cooldown for your Lay on Hands spell is reduced by {10/20} min.",
          [P_mod("Cooldown", -600, abilities=["paladin_lay_on_hands"]),
           P_proc(proc("OnAbilityUsed", [apply("paladin_improved_lay_on_hands")],
                       abilities=["paladin_lay_on_hands"]))]),
        T(h("unyielding_faith"), "Unyielding Faith", "cross", 3, 3, 2,
          "Increases your chance to resist Fear and Disorient effects by an additional {5/10}%.",
          [P_special("PaladinUnyieldingFaith", values=[5, 10])]),
        T(h("illumination"), "Illumination", "sun", 4, 1, 5,
          "After getting a critical effect from your Flash of Light, Holy Light, or Holy Shock heal spell, gives you "
          "a {20/40/60/80/100}% chance to gain mana equal to the base cost of the spell.",
          [P_proc(proc("OnSpellCrit", [E("Special", target="Self", special="PaladinIllumination")],
                       chance=20, abilities=[HL, FOL, HS]), values=[20, 40, 60, 80, 100])]),
        T(h("improved_blessing_of_wisdom"), "Improved Blessing of Wisdom", "blessing", 4, 2, 2,
          "Increases the effect of your Blessing of Wisdom spell by {10/20}%.",
          [P_mod("Effect", 10, abilities=["paladin_blessing_of_wisdom", "paladin_greater_blessing_of_wisdom"])]),
        T(h("divine_favor"), "Divine Favor", "star", 5, 1, 1,
          "When activated, gives your next Flash of Light, Holy Light, or Holy Shock spell a 100% critical effect "
          "chance.", [P_grant("paladin_divine_favor")], requires=h("illumination")),
        T(h("lasting_judgement"), "Lasting Judgement", "judgement", 5, 2, 3,
          "Increases the duration of your Judgement of Light by {10/20/30} sec.",
          [P_mod("Duration", 10, abilities=["paladin_judgement_of_light"])]),
        T(h("holy_power"), "Holy Power", "holy", 6, 2, 5,
          "Increases the critical effect chance of your Holy spells by {1/2/3/4/5}%.",
          [P_stat("SpellCrit", 1, school="Holy")]),
        T(h("holy_shock"), "Holy Shock", "holy", 7, 1, 1,
          "Blasts the target with Holy energy, causing 204 to 220 Holy damage to an enemy, or 204 to 220 healing to an "
          "ally.", [P_grant(HS)], requires=h("divine_favor")),
    ],
}

PROT = {
    "id": "tree_paladin_protection", "classId": "Paladin", "name": "Protection", "icon": "shield",
    "description": "Shields, blessings of protection, defense and holy threat.",
    "talents": [
        T(p("improved_devotion_aura"), "Improved Devotion Aura", "aura", 1, 1, 5,
          "Increases the armor bonus of your Devotion Aura by {5/10/15/20/25}%.",
          [P_mod("Effect", 5, abilities=["paladin_devotion_aura"])]),
        T(p("redoubt"), "Redoubt", "shield", 1, 2, 5,
          "Increases your chance to block attacks with your shield by {6/12/18/24/30}% after being the victim of a "
          "critical strike. Lasts 10 sec or 5 blocks.",
          [P_proc(proc("OnCritTaken", [apply("paladin_redoubt", target="Self")]))]),
        T(p("precision"), "Precision", "sword", 2, 0, 3,
          "Increases your chance to hit with melee weapons by {1/2/3}%.", [P_stat("MeleeHit", 1)]),
        T(p("guardians_favor"), "Guardian's Favor", "blessing", 2, 1, 2,
          "Reduces the cooldown of your Blessing of Protection by {60/120} sec and increases the duration of your "
          "Blessing of Freedom by {3/6} sec.",
          [P_mod("Cooldown", -60, abilities=["paladin_blessing_of_protection"]),
           P_mod("Duration", 3, abilities=["paladin_blessing_of_freedom"])]),
        T(p("toughness"), "Toughness", "armor", 2, 3, 5,
          "Increases your armor value from items by {2/4/6/8/10}%.", [P_stat("Armor", 2, pct=True)]),
        T(p("blessing_of_kings"), "Blessing of Kings", "crown", 3, 0, 1,
          "Places a Blessing on the friendly target, increasing total stats by 10% for 5 min.",
          [P_grant("paladin_blessing_of_kings"), P_grant("paladin_greater_blessing_of_kings")]),
        T(p("improved_righteous_fury"), "Improved Righteous Fury", "sun", 3, 1, 3,
          "Increases the threat generated by your Righteous Fury spell by {16/33/50}%.",
          [P_mod("Effect", values=[16, 33, 50], abilities=["paladin_righteous_fury"])]),
        T(p("shield_specialization"), "Shield Specialization", "shield", 3, 2, 3,
          "Increases the amount of damage absorbed by your shield by {10/20/30}%.",
          [P_stat("BlockValue", 10, pct=True)], requires=p("redoubt")),
        T(p("anticipation"), "Anticipation", "eye", 3, 3, 5,
          "Increases your Defense skill by {2/4/6/8/10}.", [P_stat("Defense", 2)]),
        T(p("improved_hammer_of_justice"), "Improved Hammer of Justice", "hammer", 4, 1, 3,
          "Decreases the cooldown of your Hammer of Justice spell by {5/10/15} sec.",
          [P_mod("Cooldown", -5, abilities=["paladin_hammer_of_justice"])]),
        T(p("improved_concentration_aura"), "Improved Concentration Aura", "aura", 4, 2, 3,
          "Increases the effect of your Concentration Aura by an additional {5/10/15}% and gives all group members "
          "affected by the aura an additional {5/10/15}% chance to resist Silence and Interrupt effects.",
          [P_mod("Effect", 5, abilities=["paladin_concentration_aura"])]),
        T(p("blessing_of_sanctuary"), "Blessing of Sanctuary", "blessing", 5, 1, 1,
          "Places a Blessing on the friendly target, reducing damage dealt from all sources by up to 10 for 5 min. "
          "In addition, when the target blocks a melee attack the attacker takes 14 Holy damage.",
          [P_grant("paladin_blessing_of_sanctuary"), P_grant("paladin_greater_blessing_of_sanctuary")]),
        T(p("reckoning"), "Reckoning", "fist", 5, 2, 5,
          "Gives you a {20/40/60/80/100}% chance to gain an extra attack after being the victim of a critical strike. "
          "Extra attacks stack up to 4.",
          [P_proc(proc("OnCritTaken", [E("Special", target="Self", special="PaladinReckoning")], chance=20),
                  values=[20, 40, 60, 80, 100])]),
        T(p("one_handed_weapon_specialization"), "One-Handed Weapon Specialization", "mace", 6, 2, 5,
          "Increases the damage you deal with one-handed melee weapons by {2/4/6/8/10}%.",
          [P_special("PaladinWeaponSpecialization", values=[2, 4, 6, 8, 10])]),
        T(p("holy_shield"), "Holy Shield", "shield", 7, 1, 1,
          "Increases chance to block by 30% for 10 sec, and deals 65 Holy damage for each attack blocked while active. "
          "Each block expends a charge. 4 charges.", [P_grant("paladin_holy_shield")],
          requires=p("blessing_of_sanctuary")),
    ],
}

RET = {
    "id": "tree_paladin_retribution", "classId": "Paladin", "name": "Retribution", "icon": "hammer",
    "description": "Righteous melee combat, seals and judgements.",
    "talents": [
        T(rt("improved_blessing_of_might"), "Improved Blessing of Might", "blessing", 1, 1, 5,
          "Increases the melee attack power bonus of your Blessing of Might by {4/8/12/16/20}%.",
          [P_mod("Effect", 4, abilities=["paladin_blessing_of_might", "paladin_greater_blessing_of_might"])]),
        T(rt("benediction"), "Benediction", "hands_pray", 1, 2, 5,
          "Decreases the mana cost of your Judgement and Seal spells by {3/6/9/12/15}%.",
          [P_mod("Cost", -3, tags=["Seal", "Judgement"])]),
        T(rt("improved_judgement"), "Improved Judgement", "judgement", 2, 0, 2,
          "Decreases the cooldown of your Judgement spell by {1/2} sec.",
          [P_mod("Cooldown", -1, abilities=["paladin_judgement"])]),
        T(rt("improved_seal_of_the_crusader"), "Improved Seal of the Crusader", "seal", 2, 1, 3,
          "Increases the melee attack power bonus of your Seal of the Crusader and the Holy damage increase of your "
          "Judgement of the Crusader by {5/10/15}%.",
          [P_mod("Effect", 5, abilities=["paladin_seal_of_the_crusader", "paladin_judgement_of_the_crusader"])]),
        T(rt("deflection"), "Deflection", "shield", 2, 2, 5,
          "Increases your Parry chance by {1/2/3/4/5}%.", [P_stat("Parry", 1)]),
        T(rt("vindication"), "Vindication", "fist", 3, 0, 3,
          "Gives the Paladin's damaging melee attacks a chance to reduce the target's Strength and Agility by "
          "{5/10/15}% for 10 sec.",
          [P_proc(proc("OnMeleeHit", [apply("paladin_vindication")], ppm=2))]),
        T(rt("conviction"), "Conviction", "sword", 3, 1, 5,
          "Increases your chance to get a critical strike with melee weapons by {1/2/3/4/5}%.",
          [P_stat("MeleeCrit", 1)]),
        T(rt("seal_of_command"), "Seal of Command", "seal", 3, 2, 1,
          "Gives the Paladin a chance to deal additional Holy damage equal to 70% of normal weapon damage. Lasts 30 "
          "sec. Unleashing this Seal's energy will judge an enemy for 46.5 to 50.5 Holy damage.",
          [P_grant("paladin_seal_of_command")]),
        T(rt("pursuit_of_justice"), "Pursuit of Justice", "boot", 3, 3, 2,
          "Increases movement and mounted movement speed by {4/8}%. This does not stack with other movement speed "
          "increasing effects.", [P_stat("MoveSpeed", 4, pct=True)]),
        T(rt("eye_for_an_eye"), "Eye for an Eye", "eye", 4, 0, 2,
          "All spell criticals against you cause {15/30}% of the damage taken to the caster as well. The damage "
          "caused by Eye for an Eye will not exceed 50% of the Paladin's total health.",
          [P_proc(proc("OnCritTaken", [E("Special", target="Attacker", special="PaladinEyeForAnEye")],
                       schools=MAGIC))]),
        T(rt("improved_retribution_aura"), "Improved Retribution Aura", "aura", 4, 2, 2,
          "Increases the damage done by your Retribution Aura by {25/50}%.",
          [P_mod("Effect", 25, abilities=["paladin_retribution_aura"])]),
        T(rt("two_handed_weapon_specialization"), "Two-Handed Weapon Specialization", "hammer", 5, 0, 3,
          "Increases the damage you deal with two-handed melee weapons by {2/4/6}%.",
          [P_special("PaladinWeaponSpecialization", values=[2, 4, 6])]),
        T(rt("sanctity_aura"), "Sanctity Aura", "aura", 5, 2, 1,
          "Increases Holy damage done by party members within 30 yards by 10%. Players can only have one Aura on them "
          "per Paladin at any one time.", [P_grant("paladin_sanctity_aura")]),
        T(rt("vengeance"), "Vengeance", "rage", 6, 1, 5,
          "Gives you a {3/6/9/12/15}% bonus to Physical and Holy damage you deal for 8 sec after dealing a critical "
          "strike from a weapon swing, spell, or ability.",
          [P_proc(proc("OnCrit", [apply("paladin_vengeance", target="Self")]))], requires=rt("conviction")),
        T(rt("repentance"), "Repentance", "hands_pray", 7, 1, 1,
          "Puts the enemy target in a state of meditation, incapacitating them for up to 6 sec. Any damage caused will "
          "awaken the target. Only works against Humanoids.", [P_grant("paladin_repentance")],
          requires=rt("vengeance")),
    ],
}

TREES = [HOLY, PROT, RET]

# ------------------------------------------------- auras applied by talents
# StatMod.values are indexed by the rank of the talent that applied the aura (see notes).
AURAS.append(make_aura("paladin_improved_lay_on_hands", "Improved Lay on Hands", "hands_pray", "Buff", "Holy",
                       "Armor increased.", 120, mods=[M("Armor", values=[15, 30], pct=True)]))
AURAS.append(make_aura("paladin_redoubt", "Redoubt", "shield", "Buff", "Physical",
                       "Block chance increased.", 10, charges=5,
                       mods=[M("Block", values=[6, 12, 18, 24, 30])],
                       procs=[proc("OnBlock", [], consumeCharge=True)]))
AURAS.append(make_aura("paladin_vindication", "Vindication", "fist", "Debuff", "Holy",
                       "Strength and Agility reduced.", 10, dispel="Magic",
                       mods=[M("Strength", values=[-5, -10, -15], pct=True),
                             M("Agility", values=[-5, -10, -15], pct=True)]))
AURAS.append(make_aura("paladin_vengeance", "Vengeance", "rage", "Buff", "Holy",
                       "Physical and Holy damage increased.", 8,
                       mods=[M("DamageDone", values=[3, 6, 9, 12, 15], pct=True, school="Physical"),
                             M("DamageDone", values=[3, 6, 9, 12, 15], pct=True, school="Holy")]))

DEFAULT_BUILD = (
    [rt("benediction")] * 5 + [rt("improved_judgement")] * 2 + [rt("deflection")] * 3 + [rt("seal_of_command")]
    + [rt("conviction")] * 5 + [rt("pursuit_of_justice")] * 2 + [rt("deflection")] * 2
    + [rt("two_handed_weapon_specialization")] * 3 + [rt("vindication")] * 3 + [rt("vengeance")] * 5
    + [rt("repentance")]
    + [h("divine_strength")] * 5 + [h("improved_seal_of_righteousness")] * 5 + [h("consecration")]
    + [h("divine_intellect")] * 5 + [rt("improved_blessing_of_might")] * 3
)
assert len(DEFAULT_BUILD) == 51, len(DEFAULT_BUILD)
