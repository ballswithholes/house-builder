# Priest: heals, shields, buffs, racial (human/dwarf) spells.
from common import *

ABILITIES, AURAS = [], []


def PR(*a, **k):
    ABILITIES.append(make_ability("Priest", *a, **k))


def AU(*a, **k):
    AURAS.append(make_aura(*a, **k))


# ------------------------------------------------------------------ heals
LH_R = [1, 4, 10]
PR("priest_lesser_heal", "Lesser Heal", "heal_plus", "Holy", "Heal your target for {0}.", 1, LH_R,
   castTime=2.5, cost=cost(30, 75, LH_R), target="Ally", range=40,
   effects=[heal(46, 56, 135, 157, LH_R, coef=0.714)], tags=["Heal"], aiHint="Heal", aiPriority=5)

HEAL_R = [16, 22, 28, 34]
PR("priest_heal", "Heal", "heal_plus", "Holy", "Heal your target for {0}.", 16, HEAL_R,
   castTime=3.0, cost=cost(155, 305, HEAL_R), target="Ally", range=40,
   effects=[heal(295, 341, 712, 804, HEAL_R, coef=0.857)], tags=["Heal"], aiHint="Heal", aiPriority=6)

GH_R = [40, 46, 52, 58, 60]
PR("priest_greater_heal", "Greater Heal", "heal_plus", "Holy", "A slow casting spell that heals a single target for {0}.",
   40, GH_R, castTime=3.0, cost=cost(370, 710, GH_R), target="Ally", range=40,
   effects=[heal(899, 1013, 1966, 2194, GH_R, coef=0.857)], tags=["Heal"], aiHint="Heal", aiPriority=7)

FH_R = [20, 26, 32, 38, 44, 50, 56]
PR("priest_flash_heal", "Flash Heal", "sparkle", "Holy", "Heals a friendly target for {0}.", 20, FH_R,
   castTime=1.5, cost=cost(125, 380, FH_R), target="Ally", range=40,
   effects=[heal(202, 247, 812, 958, FH_R, coef=0.429)], tags=["Heal"], aiHint="Heal", aiPriority=7)

RENEW_R = [8, 14, 20, 26, 32, 38, 44, 50, 56, 60]
PR("priest_renew", "Renew", "renew", "Holy",
   "Heals the target for 45 (rank 10: 970) over 15 sec.", 8, RENEW_R,
   cost=cost(30, 410, RENEW_R), target="Ally", range=40,
   effects=[apply("priest_renew")], tags=["Heal"], aiHint="Heal", aiPriority=6)
AU("priest_renew", "Renew", "renew", "Buff", "Holy", "Healing every 3 sec.", 15, dispel="Magic",
   exclusiveGroup="priest_renew", exclusivePerCaster=True, tickInterval=3,
   tickEffects=[E("Heal", min=9, perLevel=pl(9, 194, 8, 60), coef=0.2)])

POH_R = [30, 40, 50, 60]
PR("priest_prayer_of_healing", "Prayer of Healing", "hands_pray", "Holy",
   "A powerful prayer that heals party members within 30 yards for {0}.", 30, POH_R,
   castTime=3.0, cost=cost(410, 1030, POH_R), target="Self",
   effects=[heal(312, 333, 939, 991, POH_R, coef=0.286, target="AlliesInRadius", radius=30)],
   tags=["Heal"], aiHint="Heal", aiPriority=6)

DP_R = [10, 18, 26, 34, 42, 50, 58]
PR("priest_desperate_prayer", "Desperate Prayer", "hands_pray", "Holy",
   "Instantly heals the caster for {0}. (Human and Dwarf priest racial spell.)", 10, DP_R,
   cooldown=600, target="Self", effects=[heal(134, 170, 1601, 1887, DP_R, coef=0.429, target="Self")],
   tags=["Heal"], aiHint="Heal", aiPriority=8)

# ---------------------------------------------------------------- shields
PWS_R = [6, 12, 18, 24, 30, 36, 42, 48, 54, 60]
PR("priest_power_word_shield", "Power Word: Shield", "shield", "Holy",
   "Draws on the soul of the party member to shield them, absorbing 44 (rank 10: 942) damage. Lasts 30 sec. While "
   "the shield holds, spellcasting will not be interrupted by damage. Once shielded, the target cannot be shielded "
   "again for 15 sec (Weakened Soul).", 6, PWS_R,
   cooldown=4, cost=cost(45, 500, PWS_R), target="Ally", range=40,
   effects=[apply("priest_power_word_shield"), apply("priest_weakened_soul")],
   tags=["Shield"], special="PriestWeakenedSoulCheck", aiHint="Defensive", aiPriority=7)
AU("priest_power_word_shield", "Power Word: Shield", "shield", "Buff", "Holy",
   "Absorbs damage. Spellcasting is not delayed by damage while the shield holds.", 30, dispel="Magic",
   tags=["Shield"], absorb={"amount": 44, "perLevel": pl(44, 942, 6, 60), "coef": 0.1})
AU("priest_weakened_soul", "Weakened Soul", "soul_shard", "Debuff", "Holy",
   "Cannot be affected by Power Word: Shield.", 15, tags=["WeakenedSoul"])

# ------------------------------------------------------------------ buffs
PWF_R = [1, 12, 24, 36, 48, 60]
PR("priest_power_word_fortitude", "Power Word: Fortitude", "heart", "Holy",
   "Power infuses the target, increasing their Stamina by 3 (rank 6: 54) for 30 min.", 1, PWF_R,
   cost=cost(60, 1525, PWF_R), target="Ally", range=30, effects=[apply("priest_power_word_fortitude")],
   tags=["Buff"], aiHint="Buff", aiPriority=4)
AU("priest_power_word_fortitude", "Power Word: Fortitude", "heart", "Buff", "Holy", "Stamina increased.", 1800,
   dispel="Magic", exclusiveGroup="priest_fortitude",
   mods=[M("Stamina", values=[3, 8, 20, 32, 43, 54])])

POF_R = [48, 60]
PR("priest_prayer_of_fortitude", "Prayer of Fortitude", "heart", "Holy",
   "Power infuses all party members, increasing their Stamina by 43 (rank 2: 54) for 1 hour.", 48, POF_R,
   cost=cost(1700, 2200, POF_R), target="Self", effects=[apply("priest_prayer_of_fortitude", target="Party")],
   tags=["Buff"], aiHint="Buff", aiPriority=3)
AU("priest_prayer_of_fortitude", "Prayer of Fortitude", "heart", "Buff", "Holy", "Stamina increased.", 3600,
   dispel="Magic", exclusiveGroup="priest_fortitude", mods=[M("Stamina", values=[43, 54])])

SP_R = [30, 42, 56]
PR("priest_shadow_protection", "Shadow Protection", "shadow", "Shadow",
   "Increases the target's resistance to Shadow spells by 30 (rank 3: 60) for 10 min.", 30, SP_R,
   cost=cost(160, 450, SP_R), target="Ally", range=30, effects=[apply("priest_shadow_protection")],
   tags=["Buff"], aiHint="Buff", aiPriority=2)
AU("priest_shadow_protection", "Shadow Protection", "shadow", "Buff", "Shadow", "Shadow resistance increased.", 600,
   dispel="Magic", mods=[M("Resistance", values=[30, 45, 60], school="Shadow")])

DS_R = [30, 40, 50, 60]
PR("priest_divine_spirit", "Divine Spirit", "halo", "Holy",
   "Holy power infuses the target, increasing their Spirit by 23 (rank 4: 40) for 30 min.", 30, DS_R,
   fromTalent=True, cost=cost(285, 970, DS_R), target="Ally", range=30, effects=[apply("priest_divine_spirit")],
   tags=["Buff"], aiHint="Buff", aiPriority=4)
AU("priest_divine_spirit", "Divine Spirit", "halo", "Buff", "Holy", "Spirit increased.", 1800, dispel="Magic",
   mods=[M("Spirit", values=[23, 28, 33, 40])])

IF_R = [12, 20, 30, 40, 50, 60]
PR("priest_inner_fire", "Inner Fire", "sun", "Holy",
   "A burst of Holy energy fills the caster, increasing armor by 315 (rank 6: 1580). Lasts 10 min.", 12, IF_R,
   cost=cost(20, 245, IF_R), target="Self", effects=[apply("priest_inner_fire", target="Self")],
   tags=["Buff"], aiHint="Buff", aiPriority=4)
AU("priest_inner_fire", "Inner Fire", "sun", "Buff", "Holy", "Armor increased.", 600, dispel="Magic",
   mods=[M("Armor", values=interp(315, 1580, IF_R))])

PR("priest_fear_ward", "Fear Ward", "cross", "Holy",
   "Wards the friendly target against Fear. The next Fear effect used against the target will fail, using up the "
   "ward. Lasts 10 min. (Dwarf priest racial spell.)", 20,
   cooldown=30, cost=cost(78), target="Ally", range=30, effects=[apply("priest_fear_ward")],
   tags=["Buff"], aiHint="Buff", aiPriority=3)
AU("priest_fear_ward", "Fear Ward", "cross", "Buff", "Holy", "The next Fear effect against you fails.", 600,
   dispel="Magic", charges=1, special="PriestFearWard")
