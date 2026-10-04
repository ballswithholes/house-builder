# Priest: offensive Holy and Shadow spells (trainable and talent-granted), Feedback.
from common import *

ABILITIES, AURAS = [], []
MAGIC = ["Holy", "Fire", "Nature", "Frost", "Shadow", "Arcane"]


def PR(*a, **k):
    ABILITIES.append(make_ability("Priest", *a, **k))


def AU(*a, **k):
    AURAS.append(make_aura(*a, **k))


SMITE_R = [1, 6, 14, 22, 30, 38, 46, 54]
PR("priest_smite", "Smite", "holy", "Holy", "Smite an enemy for {0} Holy damage.", 1, SMITE_R,
   castTime=2.5, cost=cost(20, 280, SMITE_R), target="Enemy", range=30,
   effects=[dmg(13, 17, 371, 415, SMITE_R, coef=0.714)], aiHint="Damage", aiPriority=4)

HF_R = [20, 24, 30, 36, 42, 48, 54, 60]
PR("priest_holy_fire", "Holy Fire", "fire", "Holy",
   "Consumes the enemy in Holy flames that cause {0} Holy damage and an additional 30 (rank 8: 195) Holy damage over "
   "10 sec.", 20, HF_R, castTime=3.5, cost=cost(85, 290, HF_R), target="Enemy", range=30,
   effects=[dmg(84, 104, 467, 591, HF_R, coef=0.75), apply("priest_holy_fire")], aiHint="Damage", aiPriority=5)
AU("priest_holy_fire", "Holy Fire", "fire", "Debuff", "Holy", "Taking Holy damage every 2 sec.", 10, dispel="Magic",
   exclusiveGroup="priest_holy_fire", exclusivePerCaster=True, tickInterval=2,
   tickEffects=[E("Damage", min=6, perLevel=pl(6, 39, 20, 60), coef=0.034)])

SWP_R = [4, 10, 18, 26, 34, 42, 50, 58]
PR("priest_shadow_word_pain", "Shadow Word: Pain", "skull", "Shadow",
   "A word of darkness that causes 30 (rank 8: 852) Shadow damage over 18 sec.", 4, SWP_R,
   cost=cost(25, 470, SWP_R), target="Enemy", range=30, effects=[apply("priest_shadow_word_pain")],
   aiHint="Debuff", aiPriority=6)
AU("priest_shadow_word_pain", "Shadow Word: Pain", "skull", "Debuff", "Shadow", "Taking Shadow damage every 3 sec.", 18,
   dispel="Magic", exclusiveGroup="priest_shadow_word_pain", exclusivePerCaster=True, tickInterval=3,
   tickEffects=[E("Damage", min=5, perLevel=pl(5, 142, 4, 58), coef=0.183)])

MB_R = [10, 16, 22, 28, 34, 40, 46, 52, 58]
PR("priest_mind_blast", "Mind Blast", "brain", "Shadow", "Blasts the target for {0} Shadow damage.", 10, MB_R,
   castTime=1.5, cooldown=8, cost=cost(50, 350, MB_R), target="Enemy", range=30,
   effects=[dmg(39, 43, 503, 531, MB_R, coef=0.429)], aiHint="Damage", aiPriority=7)

MF_R = [20, 28, 36, 44, 52, 60]
PR("priest_mind_flay", "Mind Flay", "tentacle", "Shadow",
   "Assault the target's mind with Shadow energy, causing {0} Shadow damage per second for 3 sec (75 total at rank 1, "
   "426 at rank 6) and slowing their movement speed by 50%. Channeled; effects resolve once per tick.", 20, MF_R,
   fromTalent=True, castTime=3, channeled=True, channelTicks=3, cost=cost(45, 205, MF_R), target="Enemy", range=20,
   effects=[E("Damage", min=25, perLevel=pl(25, 142, 20, 60), coef=0.15), apply("priest_mind_flay")],
   aiHint="Damage", aiPriority=5)
AU("priest_mind_flay", "Mind Flay", "tentacle", "Debuff", "Shadow", "Movement speed reduced by 50%.", 3,
   dispel="Magic", mods=[M("MoveSpeed", -50, pct=True)], tags=["Snare"])

PS_R = [14, 28, 42, 56]
PR("priest_psychic_scream", "Psychic Scream", "fear", "Shadow",
   "The caster lets out a psychic scream, causing up to 5 enemies within 8 yards to flee for 8 sec (rank 1 affects "
   "2 enemies, +1 per rank). Damage caused may interrupt the effect.", 14, PS_R,
   cooldown=30, cost=cost(100, 210, PS_R), target="Self",
   area={"shape": "Circle", "radius": 8, "centeredOnCaster": True, "maxTargets": 5, "affects": "Enemies"},
   effects=[apply("priest_psychic_scream")], aiHint="CC", aiPriority=6)
AU("priest_psychic_scream", "Psychic Scream", "fear", "Debuff", "Shadow", "Fleeing in terror.", 8, dispel="Magic",
   states=["Fear"], tags=["Fear"])

MBURN_R = [24, 32, 40, 48, 56]
PR("priest_mana_burn", "Mana Burn", "drain", "Shadow",
   "Destroy 99-111 (rank 5: 408-452) mana from a target. For each mana destroyed in this way, the target takes 0.5 "
   "Shadow damage.", 24, MBURN_R, castTime=3.0, cost=cost(70, 230, MBURN_R), target="Enemy", range=30,
   effects=[E("Special", special="PriestManaBurn", min=99, max=111,
              perLevel=pl(105, 430, 24, 56), amount=50)], aiHint="Debuff", aiPriority=3)

HN_R = [20, 28, 36, 44, 52, 60]
PR("priest_holy_nova", "Holy Nova", "sun", "Holy",
   "Causes an explosion of holy light around the caster, causing {0} Holy damage to all enemy targets within 10 yards "
   "and healing all party members within 10 yards for {1}. These effects cause no threat.", 20, HN_R,
   fromTalent=True, cost=cost(185, 750, HN_R), target="Self",
   effects=[dmg(29, 34, 160, 186, HN_R, coef=0.107, target="EnemiesInRadius", radius=10),
            heal(52, 60, 302, 350, HN_R, coef=0.143, target="AlliesInRadius", radius=10)],
   tags=["Heal", "NoThreat"], aiHint="AoE", aiPriority=5)

PR("priest_silence", "Silence", "mask", "Shadow",
   "Silences the target, preventing them from casting spells for 5 sec. Interrupts a spell being cast.", 30,
   fromTalent=True, cooldown=45, cost=cost(225), target="Enemy", range=20,
   effects=[E("Interrupt", lockout=0), apply("priest_silence")], tags=["Interrupt"], aiHint="Interrupt",
   aiPriority=8)
AU("priest_silence", "Silence", "mask", "Debuff", "Shadow", "Silenced.", 5, dispel="Magic", states=["Silence"])

PR("priest_vampiric_embrace", "Vampiric Embrace", "blood", "Shadow",
   "Afflicts your target with Shadow energy that causes all party members to be healed for 20% of any Shadow spell "
   "damage you deal to the target. Lasts 1 min.", 30, fromTalent=True,
   cooldown=10, cost=cost(40), target="Enemy", range=30, effects=[apply("priest_vampiric_embrace")],
   aiHint="Debuff", aiPriority=5)
AU("priest_vampiric_embrace", "Vampiric Embrace", "blood", "Debuff", "Shadow",
   "Shadow damage from the priest heals their party.", 60, dispel="Magic", exclusiveGroup="priest_vampiric_embrace",
   exclusivePerCaster=True, special="PriestVampiricEmbrace")

PR("priest_shadowform", "Shadowform", "void", "Shadow",
   "Assume a Shadowform, increasing your Shadow damage by 15% and reducing Physical damage done to you by 15%. "
   "However, you may not cast Holy spells while in this form.", 40, fromTalent=True,
   cost={"type": "Mana", "pctBaseMana": 13}, target="Self", effects=[apply("priest_shadowform", target="Self")],
   tags=["Form"], aiHint="Buff", aiPriority=7)
AU("priest_shadowform", "Shadowform", "void", "Buff", "Shadow",
   "Shadow damage increased by 15%, Physical damage taken reduced by 15%. Cannot cast Holy spells.", 0,
   exclusiveGroup="priest_shadowform", tags=["Form"],
   mods=[M("DamageDone", 15, pct=True, school="Shadow"), M("DamageTaken", -15, pct=True, school="Physical")],
   forbidSchools=["Holy"])

FB_R = [20, 30, 40, 50, 60]
PR("priest_feedback", "Feedback", "eye", "Shadow",
   "The priest becomes surrounded with anti-magic energy. Any successful spell cast against the priest will burn 105 "
   "(rank 5: 805) of the attacker's mana, causing 1 Shadow damage for each point of mana burned. Lasts 15 sec. "
   "(Human priest racial spell.)", 20, FB_R,
   cooldown=180, cost=cost(80, 300, FB_R), target="Self", effects=[apply("priest_feedback", target="Self")],
   aiHint="Defensive", aiPriority=3)
AU("priest_feedback", "Feedback", "eye", "Buff", "Shadow", "Spells cast against you burn the caster's mana.", 15,
   dispel="Magic", procs=[proc("OnDamaged", [E("Special", target="Attacker", special="PriestManaBurn", min=105,
                                               perLevel=pl(105, 805, 20, 60), amount=100)], schools=MAGIC)])
