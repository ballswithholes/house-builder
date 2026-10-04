# Paladin: blessings (normal + greater) and auras.
from common import *

ABILITIES, AURAS = [], []


def PA(*a, **k):
    ABILITIES.append(make_ability("Paladin", *a, **k))


BLESS = dict(dispel="Magic", exclusiveGroup="paladin_blessing", exclusivePerCaster=True, tags=["Blessing"])


def bless_aura(id_, name, desc, duration, **kw):
    d = dict(BLESS)
    d.update(kw)
    AURAS.append(make_aura(id_, name, "blessing", "Buff", "Holy", desc, duration, **d))


def blessing(id_, name, desc, learn, ranks, c, cmax, aura_desc, aura_kw, from_talent=False, ai=4, **kw):
    PA(id_, name, "blessing", "Holy", desc, learn, ranks, cost=cost(c, cmax, ranks) if ranks else cost(c),
       target="Ally", range=30, effects=[apply(id_)] + kw.pop("extra_effects", []), tags=["Blessing"],
       aiHint="Buff", aiPriority=ai, **({"fromTalent": True} if from_talent else {}), **kw)
    bless_aura(id_, name, aura_desc, 300, **aura_kw)


def greater(id_, name, desc, learn, ranks, c, cmax, aura_desc, aura_kw, from_talent=False):
    # WoW 1.12: cast on one friendly target; the PaladinGreaterBlessing special also blesses every other party
    # member who shares the target's class (class audit + engine follow-up).
    PA(id_, name, "crown", "Holy", desc, learn, ranks, cost=cost(c, cmax, ranks) if ranks else cost(c),
       target="Ally", range=40, effects=[apply(id_)], tags=["Blessing"], special="PaladinGreaterBlessing",
       aiHint="Buff", aiPriority=3, **({"fromTalent": True} if from_talent else {}))
    bless_aura(id_, name, aura_desc, 900, **aura_kw)


# ------------------------------------------------------------- blessings
BOM_R = [4, 12, 22, 32, 42, 52, 60]
blessing("paladin_blessing_of_might", "Blessing of Might",
         "Places a Blessing on the friendly target, increasing melee attack power by 20 (rank 7: 185) for 5 min. "
         "Players may only have one Blessing on them per Paladin at any one time.", 4, BOM_R, 20, 130,
         "Melee attack power increased.", dict(mods=[M("AttackPower", values=[20, 35, 55, 85, 115, 155, 185])]))

BOW_R = [14, 24, 34, 44, 54, 60]
blessing("paladin_blessing_of_wisdom", "Blessing of Wisdom",
         "Places a Blessing on the friendly target, restoring 10 (rank 6: 33) mana every 5 seconds for 5 min. "
         "Players may only have one Blessing on them per Paladin at any one time.", 14, BOW_R, 30, 100,
         "Restores mana every 5 seconds.", dict(mods=[M("ManaRegen", values=[10, 15, 20, 25, 30, 33])]))

blessing("paladin_blessing_of_kings", "Blessing of Kings",
         "Places a Blessing on the friendly target, increasing total stats by 10% for 5 min. "
         "Players may only have one Blessing on them per Paladin at any one time.", 20, None, 75, None,
         "All stats increased by 10%.", dict(mods=[M("AllStats", 10, pct=True)]), from_talent=True, ai=5)

blessing("paladin_blessing_of_salvation", "Blessing of Salvation",
         "Places a Blessing on the friendly target, reducing the threat it generates by 30% for 5 min. "
         "Players may only have one Blessing on them per Paladin at any one time.", 26, None, 70, None,
         "Threat generated reduced by 30%.", dict(mods=[M("ThreatGenerated", -30, pct=True)]))

BOL_R = [40, 50, 60]
blessing("paladin_blessing_of_light", "Blessing of Light",
         "Places a Blessing on the friendly target, increasing the effects of Holy Light spells used on the target by "
         "up to 210 and the effect of Flash of Light spells by up to 60 (rank 3: 400 and 115) for 5 min. "
         "Players may only have one Blessing on them per Paladin at any one time.", 40, BOL_R, 85, 115,
         "Holy Light and Flash of Light heal you for more.", dict(special="PaladinBlessingOfLight"))

BOS_R = [30, 40, 50, 60]
blessing("paladin_blessing_of_sanctuary", "Blessing of Sanctuary",
         "Places a Blessing on the friendly target, reducing damage dealt from all sources by up to 10 (rank 4: 24) "
         "for 5 min. In addition, when the target blocks a melee attack the attacker takes 14 (rank 4: 35) Holy "
         "damage. Players may only have one Blessing on them per Paladin at any one time.", 30, BOS_R, 85, 120,
         "Damage taken reduced; blocking deals Holy damage to the attacker.",
         dict(special="PaladinBlessingOfSanctuary",
              procs=[proc("OnBlock", [E("Damage", target="Attacker", school="Holy", min=14,
                                        perLevel=pl(14, 35, 30, 60))])]),
         from_talent=True, ai=5)

BOP_R = [10, 24, 38]
blessing("paladin_blessing_of_protection", "Blessing of Protection",
         "A targeted party member is protected from all physical attacks for 6 sec (rank 2: 8, rank 3: 10), but "
         "during that time they cannot attack or use physical abilities. Once protected, the target cannot be made "
         "invulnerable by Divine Shield, Divine Protection or Blessing of Protection again for 1 min.",
         10, BOP_R, 25, 55, "Immune to physical attacks; cannot attack or use physical abilities.",
         dict(states=["ImmunePhysical", "Pacify"]), ai=8,
         extra_effects=[apply("paladin_forbearance")], cooldown=300, special="PaladinForbearanceCheck")
# Blessing of Protection: 6/8/10 sec by rank (ApplyAura duration + perLevel seconds, see notes)
ABILITIES[-1]["effects"][0] = apply("paladin_blessing_of_protection", duration=6, perLevel=pl(6, 10, 10, 38))
ABILITIES[-1]["aiHint"] = "Defensive"
AURAS[-1]["duration"] = 6

blessing("paladin_blessing_of_freedom", "Blessing of Freedom",
         "Places a Blessing on the friendly target, granting immunity to movement impairing effects for 10 sec.",
         18, None, 60, None, "Immune to movement impairing effects.",
         dict(special="PaladinBlessingOfFreedom"), ai=6, cooldown=20)
AURAS[-1]["duration"] = 10
ABILITIES[-1]["aiHint"] = "Defensive"

BOSAC_R = [46, 54]
blessing("paladin_blessing_of_sacrifice", "Blessing of Sacrifice",
         "Places a Blessing on the party member, transferring 30 (rank 2: 40) damage taken per hit to the Paladin "
         "for 30 sec.", 46, BOSAC_R, 75, 95, "Part of each hit taken is transferred to the Paladin.",
         dict(special="PaladinBlessingOfSacrifice"), ai=4)
AURAS[-1]["duration"] = 30
ABILITIES[-1]["target"] = "AllyOther"
ABILITIES[-1]["aiHint"] = "Defensive"

# ------------------------------------------------------- greater blessings
greater("paladin_greater_blessing_of_might", "Greater Blessing of Might",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Might, "
        "increasing melee attack power by 155 (rank 2: 185) for 15 min. Players may only have one Blessing on them "
        "per Paladin at any one time.", 52, [52, 60], 175, 205,
        "Melee attack power increased.", dict(mods=[M("AttackPower", values=[155, 185])]))
greater("paladin_greater_blessing_of_wisdom", "Greater Blessing of Wisdom",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Wisdom, "
        "restoring 30 (rank 2: 33) mana every 5 seconds for 15 min. Players may only have one Blessing on them per "
        "Paladin at any one time.", 54, [54, 60], 150, 165,
        "Restores mana every 5 seconds.", dict(mods=[M("ManaRegen", values=[30, 33])]))
greater("paladin_greater_blessing_of_kings", "Greater Blessing of Kings",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Kings, "
        "increasing total stats by 10% for 15 min. Players may only have one Blessing on them per Paladin at any "
        "one time.", 60, None,
        150, None, "All stats increased by 10%.", dict(mods=[M("AllStats", 10, pct=True)]), from_talent=True)
greater("paladin_greater_blessing_of_salvation", "Greater Blessing of Salvation",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Salvation, "
        "reducing the threat it generates by 30% for 15 min. Players may only have one Blessing on them per Paladin "
        "at any one time.", 60, None, 140, None, "Threat generated reduced by 30%.",
        dict(mods=[M("ThreatGenerated", -30, pct=True)]))
greater("paladin_greater_blessing_of_light", "Greater Blessing of Light",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Light, "
        "increasing the effects of Holy Light by up to 400 and Flash of Light by up to 115 for 15 min. Players may "
        "only have one Blessing on them per Paladin at any one time.", 60, None, 170, None,
        "Holy Light and Flash of Light heal you for more.", dict(special="PaladinBlessingOfLight"))
greater("paladin_greater_blessing_of_sanctuary", "Greater Blessing of Sanctuary",
        "Gives the friendly target and every party member who shares its class a Greater Blessing of Sanctuary, "
        "reducing damage from all sources by up to 24 for 15 min; blocking a melee attack deals 35 Holy damage to "
        "the attacker. Players may only have one Blessing on them per Paladin at any one time.", 60, None, 170, None,
        "Damage taken reduced; blocking deals Holy damage to the attacker.",
        dict(special="PaladinBlessingOfSanctuary",
             procs=[proc("OnBlock", [E("Damage", target="Attacker", school="Holy", min=35)])]),
        from_talent=True)

# ------------------------------------------------------------------ auras
AURA = dict(exclusiveGroup="paladin_aura", tags=["Aura"], radius=30, radiusAffects="Allies")


def pala_aura(id_, name, icon, school, desc, learn, ranks, effect_desc, effect_kw, from_talent=False, ai=3):
    PA(id_, name, icon, school, desc, learn, ranks, target="Self",
       effects=[apply(id_, target="Self")], tags=["Aura"], aiHint="Buff", aiPriority=ai,
       **({"fromTalent": True} if from_talent else {}))
    AURAS.append(make_aura(id_, name, icon, "Buff", school, desc, 0, radiusAura=id_ + "_effect", **AURA))
    AURAS.append(make_aura(id_ + "_effect", name, icon, "Buff", school, effect_desc, 0, tags=["Aura"], **effect_kw))


DEV_R = [1, 10, 20, 30, 40, 50, 60]
pala_aura("paladin_devotion_aura", "Devotion Aura", "aura", "Holy",
          "Gives 55 (rank 7: 735) additional armor to party members within 30 yards. Players can only have one Aura "
          "on them per Paladin at any one time.", 1, DEV_R, "Armor increased.",
          dict(mods=[M("Armor", values=interp(55, 735, DEV_R))]), ai=4)

RET_R = [16, 26, 36, 46, 56]
pala_aura("paladin_retribution_aura", "Retribution Aura", "aura", "Holy",
          "Causes 5 (rank 5: 20) Holy damage to any enemy that strikes a party member within 30 yards. Players can "
          "only have one Aura on them per Paladin at any one time.", 16, RET_R,
          "Melee attackers take Holy damage.",
          dict(procs=[proc("OnStruck", [E("Damage", target="Attacker", school="Holy", min=5,
                                          perLevel=pl(5, 20, 16, 56))])]))

pala_aura("paladin_concentration_aura", "Concentration Aura", "aura", "Holy",
          "All party members within 30 yards lose 35% less casting or channeling time when damaged. Players can only "
          "have one Aura on them per Paladin at any one time.", 22, None,
          "Casting pushback reduced by 35%.", dict(special="PaladinConcentrationAura"))

for key, school, learn, ranks in (("shadow", "Shadow", 28, [28, 40, 52]), ("frost", "Frost", 32, [32, 44, 56]),
                                  ("fire", "Fire", 36, [36, 48, 60])):
    pala_aura(f"paladin_{key}_resistance_aura", f"{school} Resistance Aura", "aura", school,
              f"Gives 30 (rank 3: 60) additional {school} resistance to all party members within 30 yards. Players "
              f"can only have one Aura on them per Paladin at any one time.", learn, ranks,
              f"{school} resistance increased.", dict(mods=[M("Resistance", values=[30, 45, 60], school=school)]),
              ai=2)

pala_aura("paladin_sanctity_aura", "Sanctity Aura", "aura", "Holy",
          "Increases Holy damage done by party members within 30 yards by 10%. Players can only have one Aura on "
          "them per Paladin at any one time.", 30, None, "Holy damage done increased by 10%.",
          dict(mods=[M("DamageDone", 10, pct=True, school="Holy")]), from_talent=True, ai=4)
