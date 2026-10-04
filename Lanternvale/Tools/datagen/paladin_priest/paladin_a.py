# Paladin: heals, seals, Judgement and the judgement effects.
from common import *

ABILITIES, AURAS = [], []


def PA(*a, **k):
    ABILITIES.append(make_ability("Paladin", *a, **k))


def AU(*a, **k):
    AURAS.append(make_aura(*a, **k))


# ------------------------------------------------------------------ heals
HL_R = [1, 6, 14, 22, 30, 38, 46, 54, 60]
PA("paladin_holy_light", "Holy Light", "holy", "Holy",
   "Heals a friendly target for {0}.", 1, HL_R,
   castTime=2.5, cost=cost(35, 660, HL_R), target="Ally", range=40,
   effects=[heal(39, 47, 1590, 1770, HL_R, coef=0.714)],
   tags=["Heal"], aiHint="Heal", aiPriority=6)

FOL_R = [20, 26, 34, 42, 50, 58]
PA("paladin_flash_of_light", "Flash of Light", "sparkle", "Holy",
   "Heals a friendly target for {0}.", 20, FOL_R,
   castTime=1.5, cost=cost(35, 140, FOL_R), target="Ally", range=40,
   effects=[heal(67, 77, 348, 389, FOL_R, coef=0.429)],
   tags=["Heal"], aiHint="Heal", aiPriority=7)

# ------------------------------------------------------------------ seals
SEAL = dict(kind="Buff", school="Holy", duration=30, exclusiveGroup="paladin_seal", tags=["Seal"])


def seal(id_, name, desc, learn, ranks, c, cmax, aura_desc, aura_kw, ai=7):
    PA(id_, name, "seal", "Holy", desc, learn, ranks, cost=cost(c, cmax, ranks), target="Self",
       effects=[apply(id_, target="Self")], tags=["Seal"], aiHint="Buff", aiPriority=ai)
    kw = dict(SEAL)
    kw.update(aura_kw)
    AURAS.append(make_aura(id_, name, "seal", kw.pop("kind"), kw.pop("school"), aura_desc, kw.pop("duration"), **kw))


def judgement(id_, name, desc, learn, ranks, effects, **kw):
    PA(id_, name, "judgement", "Holy", desc, learn, ranks, hidden=True, target="Enemy", range=10,
       effects=effects, tags=["Judgement"], aiHint="Damage", aiPriority=0, **kw)


SOR_R = [1, 10, 18, 26, 34, 42, 50, 58]
seal("paladin_seal_of_righteousness", "Seal of Righteousness",
     "Fills the Paladin with holy spirit for 30 sec. Each melee hit deals additional Holy damage (3 at rank 1, about 60 "
     "at rank 8 with a 3.0 speed weapon). Judgement unleashes the seal for 15-17 (rank 8: 205-226) Holy damage.",
     1, SOR_R, 20, 130,
     "Melee hits deal additional Holy damage.",
     dict(procs=[proc("OnMeleeHit", [E("Damage", school="Holy", min=3, perLevel=pl(3, 60, 1, 58), coef=0.1)])]))
judgement("paladin_judgement_of_righteousness", "Judgement of Righteousness",
          "Unleashes the Seal of Righteousness, dealing {0} Holy damage to the target.", 1, SOR_R,
          [dmg(15, 17, 205, 226, SOR_R, coef=0.5)])

SOTC_R = [6, 12, 22, 32, 42, 52]
SOTC_AP = [36, 70, 100, 140, 200, 306]
seal("paladin_seal_of_the_crusader", "Seal of the Crusader",
     "Fills the Paladin with the spirit of a crusader for 30 sec, granting 36 melee attack power (rank 6: 306) and 40% "
     "increased melee attack speed; each melee hit deals proportionally less damage. Judgement increases the Holy "
     "damage the target takes by up to 23 (rank 6: 140) for 10 sec.",
     6, SOTC_R, 25, 160,
     "Melee attack power increased; attack speed increased by 40% but each hit deals less damage.",
     dict(mods=[M("AttackPower", values=SOTC_AP), M("MeleeHaste", 40, pct=True),
                M("DamageDone", -28.6, pct=True, school="Physical")]))
judgement("paladin_judgement_of_the_crusader", "Judgement of the Crusader",
          "Increases the Holy damage the target takes from all sources by up to 23 (rank 6: 140) for 10 sec.",
          6, SOTC_R, [apply("paladin_judgement_of_the_crusader_debuff")])
AURAS.append(make_aura("paladin_judgement_of_the_crusader_debuff", "Judgement of the Crusader", "judgement", "Debuff",
                       "Holy", "Holy damage taken increased (23/35/58/92/127/140 by rank).", 10,
                       exclusiveGroup="paladin_judgement", exclusivePerCaster=True, tags=["Judgement"],
                       special="PaladinJudgementOfTheCrusader"))

# Seal of Justice has a single rank in WoW 1.12 (class audit)
seal("paladin_seal_of_justice", "Seal of Justice",
     "Fills the Paladin with the spirit of justice for 30 sec, giving each melee attack a chance to stun the target "
     "for 2 sec. Judgement prevents the target from fleeing and restricts its movement speed to normal for 10 sec.",
     22, None, 35, 60,
     "Melee attacks have a chance to stun the target for 2 sec.",
     dict(procs=[proc("OnMeleeHit", [apply("paladin_seal_of_justice_stun")], ppm=1.0)]))
AURAS.append(make_aura("paladin_seal_of_justice_stun", "Seal of Justice", "seal", "Debuff", "Holy",
                       "Stunned.", 2, dispel="Magic", states=["Stun"], tags=["Stun"]))
judgement("paladin_judgement_of_justice", "Judgement of Justice",
          "Prevents the target from fleeing and restricts its movement speed to normal for 10 sec.", 22, None,
          [apply("paladin_judgement_of_justice_debuff")])
AURAS.append(make_aura("paladin_judgement_of_justice_debuff", "Judgement of Justice", "judgement", "Debuff", "Holy",
                       "Cannot flee; movement speed cannot exceed normal.", 10,
                       exclusiveGroup="paladin_judgement", exclusivePerCaster=True, tags=["Judgement"],
                       special="PaladinJudgementOfJustice"))

SOL_R = [30, 40, 50, 60]
seal("paladin_seal_of_light", "Seal of Light",
     "Fills the Paladin with divine light for 30 sec, giving each melee attack a chance to heal the Paladin for 39 "
     "(rank 4: 85). Judgement makes melee and ranged attacks against the target heal the attacker for 25 (rank 4: 61) "
     "50% of the time, for 10 sec.",
     30, SOL_R, 45, 110,
     "Melee attacks have a chance to heal you.",
     dict(procs=[proc("OnMeleeHit", [E("Heal", target="Self", min=39, perLevel=pl(39, 85, 30, 60))], ppm=15)]))
judgement("paladin_judgement_of_light", "Judgement of Light",
          "For 10 sec, attacks against the target have a 50% chance to heal the attacker for 25 (rank 4: 61).",
          30, SOL_R, [apply("paladin_judgement_of_light_debuff")])
AURAS.append(make_aura("paladin_judgement_of_light_debuff", "Judgement of Light", "judgement", "Debuff", "Holy",
                       "Attacks against this target may heal the attacker.", 10,
                       exclusiveGroup="paladin_judgement", exclusivePerCaster=True, tags=["Judgement"],
                       procs=[proc("OnStruck", [E("Heal", target="Attacker", min=25, perLevel=pl(25, 61, 30, 60))],
                                   chance=50)]))

SOW_R = [38, 48, 58]
seal("paladin_seal_of_wisdom", "Seal of Wisdom",
     "Fills the Paladin with divine wisdom for 30 sec, giving each melee attack a chance to restore 27 (rank 3: 50) "
     "of the Paladin's mana. Judgement makes melee and ranged attacks against the target restore 33 (rank 3: 59) mana "
     "to the attacker 50% of the time, for 10 sec.",
     38, SOW_R, 70, 107,
     "Melee attacks have a chance to restore mana.",
     dict(procs=[proc("OnMeleeHit", [E("GainResource", target="Self", resource="Mana", amount=27,
                                       perLevel=pl(27, 50, 38, 58))], ppm=15)]))
judgement("paladin_judgement_of_wisdom", "Judgement of Wisdom",
          "For 10 sec, attacks against the target have a 50% chance to restore 33 (rank 3: 59) mana to the attacker.",
          38, SOW_R, [apply("paladin_judgement_of_wisdom_debuff")])
AURAS.append(make_aura("paladin_judgement_of_wisdom_debuff", "Judgement of Wisdom", "judgement", "Debuff", "Holy",
                       "Attacks against this target may restore mana to the attacker.", 10,
                       exclusiveGroup="paladin_judgement", exclusivePerCaster=True, tags=["Judgement"],
                       procs=[proc("OnStruck", [E("GainResource", target="Attacker", resource="Mana", amount=33,
                                                  perLevel=pl(33, 59, 38, 58))], chance=50)]))

SOC_R = [20, 30, 40, 50, 60]
PA("paladin_seal_of_command", "Seal of Command", "seal", "Holy",
   "Gives the Paladin a chance to deal additional Holy damage equal to 70% of normal weapon damage (7 procs per "
   "minute) for 30 sec. Judgement deals 46-50 (rank 5: 228-252) Holy damage, doubled against stunned targets.",
   20, SOC_R, fromTalent=True, cost=cost(65, 145, SOC_R), target="Self",
   effects=[apply("paladin_seal_of_command", target="Self")], tags=["Seal"], aiHint="Buff", aiPriority=7)
AURAS.append(make_aura("paladin_seal_of_command", "Seal of Command", "seal", "Buff", "Holy",
                       "Melee hits may deal 70% weapon damage as additional Holy damage.", 30,
                       exclusiveGroup="paladin_seal", tags=["Seal"],
                       procs=[proc("OnMeleeHit", [E("WeaponDamage", school="Holy", weaponPct=70)], ppm=7)]))
judgement("paladin_judgement_of_command", "Judgement of Command",
          "Unleashes the Seal of Command, dealing {0} Holy damage to the target; doubled if the target is stunned.",
          20, SOC_R, [dmg(46, 50, 228, 252, SOC_R, coef=0.43, special="PaladinDoubleVsStunned")], fromTalent=True)

# ------------------------------------------------------------------ Judgement
PA("paladin_judgement", "Judgement", "judgement", "Holy",
   "Unleashes the energy of your active Seal upon an enemy, consuming the Seal and producing its Judgement: "
   "Righteousness and Command deal Holy damage, the Crusader increases Holy damage taken, Justice prevents fleeing, "
   "Light and Wisdom return health or mana to attackers. Judgement debuffs last 10 sec and only one of your "
   "Judgements can be on a target. Does not trigger the global cooldown.",
   4, time="OffGcd", cooldown=10, cost={"type": "Mana", "pctBaseMana": 5},
   target="Enemy", range=10, requires={"casterAuraTags": ["Seal"]},
   effects=[E("Special", special="PaladinJudgement")], tags=["Judgement"], aiHint="Damage", aiPriority=6)

# Forbearance (shared by Blessing of Protection, Divine Protection, Divine Shield)
AURAS.append(make_aura("paladin_forbearance", "Forbearance", "hand", "Debuff", "Holy",
                       "Cannot be affected by Divine Shield, Divine Protection or Blessing of Protection.", 60,
                       tags=["Forbearance"]))
