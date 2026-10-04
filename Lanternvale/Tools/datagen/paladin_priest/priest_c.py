# Priest: utility, crowd control, resurrection, talent cooldowns, Lightwell, Spirit of Redemption.
from common import *

ABILITIES, AURAS = [], []


def PR(*a, **k):
    ABILITIES.append(make_ability("Priest", *a, **k))


def AU(*a, **k):
    AURAS.append(make_aura(*a, **k))


FADE_R = [8, 16, 24, 32, 40, 48, 56]
PR("priest_fade", "Fade", "stealth", "Shadow",
   "Fade out, temporarily reducing all your threat by 55 (rank 7: 820) for 10 sec.", 8, FADE_R,
   cooldown=30, cost=cost(45, 330, FADE_R), target="Self",
   effects=[apply("priest_fade", target="Self"),
            E("Special", target="Self", special="PriestFade", min=55, perLevel=pl(55, 820, 8, 56))],
   aiHint="Defensive", aiPriority=6)
AU("priest_fade", "Fade", "stealth", "Buff", "Shadow", "Threat temporarily reduced.", 10)

DM_R = [18, 36]
PR("priest_dispel_magic", "Dispel Magic", "sparkle", "Holy",
   "Dispels magic on the target, removing 1 harmful spell from a friend or 1 beneficial spell from an enemy "
   "(rank 2: 2 spells).", 18, DM_R, cost=cost(125, 200, DM_R), target="Any", range=30,
   effects=[E("Dispel", dispelType="Magic", dispelCount=1, special="PriestDispelRank")],
   aiHint="Utility", aiPriority=5)

PR("priest_cure_disease", "Cure Disease", "water_drop", "Holy", "Removes 1 disease from the friendly target.", 14,
   cost=cost(75), target="Ally", range=30, effects=[E("Dispel", dispelType="Disease", dispelCount=1)],
   aiHint="Utility", aiPriority=4)

PR("priest_abolish_disease", "Abolish Disease", "water_drop", "Holy",
   "Attempts to cure 1 disease effect on the target, and 1 more disease effect every 5 seconds for 20 sec.", 32,
   cost=cost(105), target="Ally", range=30,
   effects=[E("Dispel", dispelType="Disease", dispelCount=1), apply("priest_abolish_disease")],
   aiHint="Utility", aiPriority=5)
AU("priest_abolish_disease", "Abolish Disease", "water_drop", "Buff", "Holy", "Removes a disease every 5 sec.", 20,
   dispel="Magic", tickInterval=5, tickEffects=[E("Dispel", dispelType="Disease", dispelCount=1)])

MS_R = [20, 34, 48]
PR("priest_mind_soothe", "Mind Soothe", "brain", "Shadow",
   "Soothes the target, reducing the range at which it will attack you by 10 yards. Only affects Humanoid targets. "
   "Lasts 15 sec.", 20, MS_R, cost=cost(70, 120, MS_R), target="Enemy", range=40,
   requires={"targetCreatureTypes": ["Humanoid"]},
   effects=[apply("priest_mind_soothe"), E("Special", special="PriestMindSoothe")], aiHint="Utility", aiPriority=0)
AU("priest_mind_soothe", "Mind Soothe", "brain", "Debuff", "Shadow", "Aggro range reduced by 10 yards.", 15,
   dispel="Magic")

SH_R = [20, 40, 60]
PR("priest_shackle_undead", "Shackle Undead", "lock", "Holy",
   "Shackles the target undead enemy for up to 30 sec (rank 2: 40, rank 3: 50). The shackled unit is unable to move, "
   "attack or cast spells. Any damage caused will release the target. Only one target can be shackled at a time.",
   20, SH_R, castTime=1.5, cost=cost(80, 150, SH_R), target="Enemy", range=30,
   requires={"targetCreatureTypes": ["Undead"]},
   effects=[apply("priest_shackle_undead", duration=30, perLevel=pl(30, 50, 20, 60))], aiHint="CC", aiPriority=6)
AU("priest_shackle_undead", "Shackle Undead", "lock", "Debuff", "Holy", "Shackled.", 30, dispel="Magic",
   exclusiveGroup="priest_shackle", exclusivePerCaster=True, states=["Incapacitate"], breakOnDamage=True)

MC_R = [30, 44, 58]
PR("priest_mind_control", "Mind Control", "eye", "Shadow",
   "Controls a humanoid mind up to level 33 (rank 2: 45, rank 3: 57), but increases the time between its attacks by "
   "25%. Lasts while the Priest channels, up to 60 sec.", 30, MC_R,
   castTime=3.0, cost=cost(350, 750, MC_R), target="Enemy", range=20,
   requires={"targetCreatureTypes": ["Humanoid"]},
   effects=[apply("priest_mind_control"), E("Special", special="PriestMindControl")], aiHint="CC", aiPriority=3)
AU("priest_mind_control", "Mind Control", "eye", "Debuff", "Shadow",
   "Controlled by a priest. Attack speed reduced by 25%.", 60, dispel="Magic",
   mods=[M("MeleeHaste", -25, pct=True)], tags=["MindControl"])

RES_R = [10, 22, 34, 46, 58]
PR("priest_resurrection", "Resurrection", "halo", "Holy",
   "Brings a dead player back to life with 35% health and mana.", 10, RES_R,
   castTime=10, cost=cost(125, 1000, RES_R), target="DeadAlly", range=30,
   effects=[E("Resurrect", pctOfMax=35)], aiHint="Utility", aiPriority=8)

# ------------------------------------------------------- talent abilities
PR("priest_inner_focus", "Inner Focus", "eye", "Holy",
   "When activated, reduces the mana cost of your next spell by 100% and increases its critical effect chance by 25% "
   "if it is capable of a critical effect. Does not trigger the global cooldown.", 20, fromTalent=True,
   time="OffGcd", cooldown=180, target="Self", effects=[apply("priest_inner_focus", target="Self")],
   aiHint="Buff", aiPriority=5)
AU("priest_inner_focus", "Inner Focus", "eye", "Buff", "Holy",
   "Next spell costs no mana and has +25% critical effect chance.", 0, charges=1,
   mods=[M("ManaCost", -100, pct=True), M("SpellCrit", 25)], procs=[proc("OnSpellCast", [], consumeCharge=True)])

PR("priest_power_infusion", "Power Infusion", "star", "Holy",
   "Infuses the target with power, increasing their spell damage and healing by 20%. Lasts 15 sec.", 40,
   fromTalent=True, cooldown=180, cost={"type": "Mana", "pctBaseMana": 16}, target="Ally", range=30,
   effects=[apply("priest_power_infusion")], aiHint="Buff", aiPriority=6)
AU("priest_power_infusion", "Power Infusion", "star", "Buff", "Holy", "Spell damage and healing increased by 20%.",
   15, dispel="Magic",
   mods=[M("DamageDone", 20, pct=True, school=s) for s in ("Holy", "Fire", "Nature", "Frost", "Shadow", "Arcane")]
   + [M("HealingDone", 20, pct=True)])

LW_R = [40, 50, 60]
PR("priest_lightwell", "Lightwell", "halo", "Holy",
   "Creates a holy Lightwell. Members of your party within 5 yards can use the Lightwell to be healed for 800 "
   "(rank 3: 1400) over 10 sec. Lightwell lasts 3 min or 5 charges.", 40, LW_R,
   fromTalent=True, castTime=1.5, cooldown=300, cost=cost(225, 345, LW_R), target="Self",
   effects=[E("Summon", target="Self", summon="priest_lightwell", lifetime=180, count=1),
            E("Special", target="Self", special="PriestLightwell")], aiHint="Summon", aiPriority=4)
PR("priest_lightwell_renew", "Lightwell Renew", "renew", "Holy",
   "Touch the Lightwell to be healed for 800 (rank 3: 1400) over 10 sec. Uses one of the Lightwell's charges.", 40,
   LW_R, hidden=True, fromTalent=True, time="OffGcd", target="Self",
   effects=[apply("priest_lightwell_renew", target="Self")], tags=["Heal"], aiHint="Heal", aiPriority=6)
AU("priest_lightwell_renew", "Lightwell Renew", "renew", "Buff", "Holy", "Healing every 2 sec.", 10, dispel="Magic",
   tickInterval=2, tickEffects=[E("Heal", min=160, perLevel=pl(160, 280, 40, 60), coef=0.067)])

PR("priest_spirit_of_redemption", "Spirit of Redemption", "wings", "Holy",
   "Upon death, the priest becomes the Spirit of Redemption for 10 sec. The Spirit of Redemption cannot move, attack, "
   "be attacked or targeted by any spells or effects. While in this form the priest can cast any healing spell free "
   "of cost. When the effect ends, the priest dies.", 30, fromTalent=True, passive=True,
   passiveAura="priest_spirit_of_redemption_passive")
AU("priest_spirit_of_redemption_passive", "Spirit of Redemption", "wings", "Buff", "Holy",
   "Upon death, become the Spirit of Redemption.", 0, hidden=True, persistThroughDeath=True,
   special="PriestSpiritOfRedemption")
AU("priest_spirit_of_redemption", "Spirit of Redemption", "wings", "Buff", "Holy",
   "Spirit form: healing spells cost no mana; cannot move, attack or be targeted.", 10,
   persistThroughDeath=True, states=["Untargetable", "Invulnerable", "Root", "Pacify"],
   mods=[M("ManaCost", -100, pct=True)])
