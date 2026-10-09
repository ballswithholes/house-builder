"""Generates Lanternvale/Assets/Lanternvale/Resources/Data/classes/hunter.json (WoW Classic 1.12 Hunter)."""
import sys
from common import *

OUT = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data/classes/hunter.json"
B = Builder("Hunter")
A, U = B.ability, B.aura

SHOT = dict(range=35, minRange=8)
RANGED = {"rangedWeapon": True}
MELEE = {"meleeWeapon": True}
STING = dict(kind="Debuff", school="Nature", dispel="Poison", exclusiveGroup="hunter_sting", exclusivePerCaster=True,
             tags=["Sting"])
CC_IMMUNE = ["Stun", "Root", "Silence", "Pacify", "Disarm", "Fear", "Polymorph", "Incapacitate", "Sleep", "Confuse", "Daze"]

# =====================================================================================================================
# Melee
# =====================================================================================================================
A("hunter_raptor_strike", "Raptor Strike", "claw", "Physical",
  "A strong attack that increases melee damage by 5 (rank 1) to 140 (rank 8). Replaces your next main-hand swing: "
  "it resolves with your auto attacks at the end of your turn and costs no Time. Next swing: {0} damage.",
  learn=1, ranks=[1, 8, 16, 24, 32, 40, 48, 56], trainCost=10,
  time="OffGcd", cooldown=6, cost=mana(15, pl(15, 100, 1, 56)), target="Enemy", melee=True, requires=MELEE,
  effects=[eff("WeaponDamage", weaponPct=100, min=5, perLevel=pl(5, 140, 1, 56))],
  tags=["Melee"], nextSwing=True, aiHint="Damage", aiPriority=5)

A("hunter_wing_clip", "Wing Clip", "feather", "Physical",
  "Maims the enemy, causing {0} damage and reducing the enemy's movement speed by 50% for 10 sec.",
  learn=12, ranks=[12, 38, 60], cost=mana(40, pl(40, 80, 12, 60)), target="Enemy", melee=True, requires=MELEE,
  effects=[eff("Damage", min=5, perLevel=pl(5, 50, 12, 60)), eff("ApplyAura", aura="hunter_wing_clip")],
  tags=["Melee"], aiHint="Debuff", aiPriority=4)
U("hunter_wing_clip", "Wing Clip", "feather", "Movement speed reduced by 50%.", kind="Debuff",
  duration=10, mods=[mod("MoveSpeed", -50)])

A("hunter_mongoose_bite", "Mongoose Bite", "fang", "Physical",
  "Attack the enemy for {0} damage. Can only be performed after you dodge (the window lasts until the end of your "
  "next turn).",
  learn=16, ranks=[16, 30, 44, 58], cooldown=5, cost=mana(30, pl(30, 65, 16, 58)), target="Enemy", melee=True,
  requires={"meleeWeapon": True, "reactive": "SelfDodged"},
  effects=[eff("Damage", min=25, perLevel=pl(25, 115, 16, 58))],
  tags=["Melee"], aiHint="Damage", aiPriority=8)

A("hunter_disengage", "Disengage", "boot", "Physical",
  "Attempts to disengage from the target, reducing your threat against it by 140 (rank 1) to 405 (rank 3).",
  learn=20, ranks=[20, 36, 52], cooldown=5, cost=mana(50, pl(50, 110, 20, 52)), target="Enemy", melee=True,
  effects=[eff("Threat", threat=-140, perLevel=pl(-140, -405, 20, 52))],
  aiHint="Defensive", aiPriority=3)

# =====================================================================================================================
# Shots
# =====================================================================================================================
A("hunter_arcane_shot", "Arcane Shot", "arcane", "Arcane",
  "An instant shot that causes {0} Arcane damage.",
  learn=6, ranks=[6, 12, 20, 28, 36, 44, 52, 60], cooldown=6, cost=mana(25, pl(25, 190, 6, 60)), target="Enemy",
  requires=RANGED, effects=[eff("Damage", min=13, perLevel=pl(13, 183, 6, 60), coef=0.429)],
  tags=["Shot"], aiHint="Damage", aiPriority=6, **SHOT)

A("hunter_concussive_shot", "Concussive Shot", "arrow", "Physical",
  "Dazes the target, slowing movement to 50% for 4 sec.",
  learn=8, cooldown=12, cost=mana(35), target="Enemy", requires=RANGED,
  effects=[eff("ApplyAura", aura="hunter_concussive_shot")],
  tags=["Shot"], aiHint="Debuff", aiPriority=3, **SHOT)
U("hunter_concussive_shot", "Concussive Shot", "arrow", "Dazed: movement speed reduced by 50%.", kind="Debuff",
  duration=4, states=["Daze"])

A("hunter_distracting_shot", "Distracting Shot", "arrows", "Physical",
  "Distracts the target to attack you (taunt) and causes 110 (rank 1) to 600 (rank 6) threat.",
  learn=12, ranks=[12, 20, 30, 40, 50, 60], cooldown=8, cost=mana(30, pl(30, 85, 12, 60)), target="Enemy",
  requires=RANGED,
  effects=[eff("Taunt"), eff("Threat", threat=110, perLevel=pl(110, 600, 12, 60))],
  tags=["Shot", "Taunt"], aiHint="Taunt", aiPriority=2, **SHOT)

A("hunter_multi_shot", "Multi-Shot", "multishot", "Physical",
  "Fires several missiles, hitting up to 3 targets (the target and enemies within 8 yards of it) for weapon damage "
  "plus 0 (rank 1) to 150 (rank 5): {0}.",
  learn=18, ranks=[18, 30, 42, 54, 60], cooldown=10, cost=mana(100, pl(100, 230, 18, 60)), target="Enemy",
  requires=RANGED, area={"shape": "Circle", "radius": 8, "maxTargets": 3, "affects": "Enemies"},
  effects=[eff("WeaponDamage", weaponPct=100, ranged=True, min=0, perLevel=pl(0, 150, 18, 60))],
  tags=["Shot"], aiHint="AoE", aiPriority=6, **SHOT)

A("hunter_volley", "Volley", "arrows", "Arcane",
  "Continuously fires a volley of ammo at the target area, causing {0} Arcane damage to enemy targets within "
  "8 yards every second for 6 sec (channeled; 6 ticks).",
  learn=40, ranks=[40, 50, 58], cooldown=60, channeled=True, castTime=6, channelTicks=6,
  cost=mana(350, pl(350, 490, 40, 58)), target="Point", range=35, requires=RANGED,
  area={"shape": "Circle", "radius": 8, "affects": "Enemies"},
  effects=[eff("Damage", min=37, perLevel=pl(37, 70, 40, 58), coef=0.03)],
  aiHint="AoE", aiPriority=5)

A("hunter_tranquilizing_shot", "Tranquilizing Shot", "arrow", "Nature",
  "Attempts to remove 1 Frenzy (enrage) effect from an enemy creature.",
  learn=60, cooldown=20, cost=mana(200), target="Enemy", requires=RANGED,
  effects=[eff("RemoveAura", auraTag="Frenzy")],
  tags=["Shot"], aiHint="Utility", aiPriority=5, **SHOT)

A("hunter_hunters_mark", "Hunter's Mark", "eye", "Arcane",
  "Places the Hunter's Mark on the target, increasing the ranged attack power of all attackers against that target "
  "by 20 (rank 1), 45, 75 or 110 (rank 4). The marked target cannot hide from the hunter. Lasts 2 min.",
  learn=6, ranks=[6, 22, 40, 58], cost=mana(15, pl(15, 60, 6, 58)), target="Enemy", range=100,
  effects=[eff("ApplyAura", aura="hunter_hunters_mark")],
  tags=["Mark"], aiHint="Debuff", aiPriority=7)
U("hunter_hunters_mark", "Hunter's Mark", "eye",
  "Ranged attacks against this target gain bonus ranged attack power. Always visible to the hunter.",
  kind="Debuff", school="Arcane", dispel="Magic", duration=120, exclusiveGroup="hunter_mark", tags=["Mark"],
  special="HunterMark")

A("hunter_rapid_fire", "Rapid Fire", "clock", "Physical",
  "Increases ranged attack speed by 40% for 15 sec.",
  learn=26, time="OffGcd", cooldown=300, cost=mana(100), target="Self", requires=RANGED,
  effects=[eff("ApplyAura", aura="hunter_rapid_fire")], aiHint="Buff", aiPriority=6)
U("hunter_rapid_fire", "Rapid Fire", "clock", "Ranged attack speed increased by 40%.", duration=15,
  mods=[mod("RangedHaste", 40)])

A("hunter_flare", "Flare", "star", "Arcane",
  "Exposes all hidden and invisible enemies within 10 yards of the targeted area.",
  learn=32, cooldown=20, cost=mana(50), target="Point", range=30,
  area={"shape": "Circle", "radius": 10, "affects": "Enemies"},
  effects=[eff("RemoveAura", auraTag="Stealth"), eff("RemoveAura", auraTag="Invisible")],
  aiHint="Utility", aiPriority=2)

A("hunter_scare_beast", "Scare Beast", "fear", "Nature",
  "Scares a beast, causing it to run in fear for up to 20 sec. Damage caused may interrupt the effect.",
  learn=14, ranks=[14, 30, 46], castTime=1.5, cost=mana(50, pl(50, 100, 14, 46)), target="Enemy", range=30,
  requires={"targetCreatureTypes": ["Beast"]},
  effects=[eff("ApplyAura", aura="hunter_scare_beast")], tags=["Fear"], aiHint="CC", aiPriority=4)
U("hunter_scare_beast", "Scare Beast", "fear", "Running in fear.", kind="Debuff", school="Nature", dispel="Magic",
  duration=20, tags=["Fear"], states=["Fear"], breakOnDamage=True, breakDamageThreshold=0)

# ---------------------------------------------------------------------------------------------------------- stings
A("hunter_serpent_sting", "Serpent Sting", "snake", "Nature",
  "Stings the target, causing {t} Nature damage every 3 sec for 15 sec (5 ticks). Only one Sting per Hunter can be "
  "active on any one target.".replace("{t}", "4 (rank 1) to 98 (rank 8)"),
  learn=4, ranks=[4, 10, 18, 26, 34, 42, 50, 58], cost=mana(15, pl(15, 230, 4, 58)), target="Enemy", requires=RANGED,
  effects=[eff("ApplyAura", aura="hunter_serpent_sting")], tags=["Sting"], aiHint="Debuff", aiPriority=6, **SHOT)
U("hunter_serpent_sting", "Serpent Sting", "snake", "Suffering Nature damage every 3 sec.", duration=15,
  tickInterval=3, tickEffects=[eff("Damage", min=4, perLevel=pl(4, 98, 4, 58), coef=0.08)], **STING)

A("hunter_scorpid_sting", "Scorpid Sting", "scorpion", "Nature",
  "Stings the target, reducing Strength and Agility by 15 (rank 1) to 60 (rank 4) for 20 sec. Only one Sting per "
  "Hunter can be active on any one target.",
  learn=22, ranks=[22, 32, 42, 52], cost=mana(30, pl(30, 75, 22, 52)), target="Enemy", requires=RANGED,
  effects=[eff("ApplyAura", aura="hunter_scorpid_sting")], tags=["Sting"], aiHint="Debuff", aiPriority=3, **SHOT)
U("hunter_scorpid_sting", "Scorpid Sting", "scorpion", "Strength and Agility reduced.", duration=20,
  mods=[mod("Strength", values=[-15, -30, -45, -60]), mod("Agility", values=[-15, -30, -45, -60])], **STING)

A("hunter_viper_sting", "Viper Sting", "drain", "Nature",
  "Stings the target, draining 132 (rank 1) to 304 (rank 3) mana over 8 sec and energizing the Hunter equal to the "
  "amount drained. Only one Sting per Hunter can be active on any one target.",
  learn=36, ranks=[36, 46, 56], cost=mana(50, pl(50, 115, 36, 56)), target="Enemy", requires=RANGED,
  effects=[eff("ApplyAura", aura="hunter_viper_sting")], tags=["Sting"], aiHint="Debuff", aiPriority=4, **SHOT)
U("hunter_viper_sting", "Viper Sting", "drain", "Mana drained every 2 sec.", duration=8, tickInterval=2,
  tickEffects=[eff("DrainResource", resource="Mana", amount=33, perLevel=pl(33, 76, 36, 56), pctToCaster=100)],
  **STING)

# =====================================================================================================================
# Aspects (permanent until changed, one at a time)
# =====================================================================================================================
ASPECT = dict(school="Nature", duration=0, exclusiveGroup="hunter_aspect", tags=["Aspect"])
A("hunter_aspect_of_the_monkey", "Aspect of the Monkey", "monkey", "Nature",
  "The hunter takes on the aspects of a monkey, increasing chance to dodge by 8%. Only one Aspect can be active at "
  "a time.", learn=4, cost=mana(20), target="Self", effects=[eff("ApplyAura", aura="hunter_aspect_of_the_monkey")],
  tags=["Aspect"], aiHint="Buff", aiPriority=2)
U("hunter_aspect_of_the_monkey", "Aspect of the Monkey", "monkey", "Dodge chance increased by 8%.",
  mods=[mod("Dodge", 8)], **ASPECT)

A("hunter_aspect_of_the_hawk", "Aspect of the Hawk", "hawk", "Nature",
  "The hunter takes on the aspects of a hawk, increasing ranged attack power by 20, 35, 50, 70, 90, 110 or 120 "
  "(by rank). Only one Aspect can be active at a time.",
  learn=10, ranks=[10, 18, 28, 38, 48, 58, 60], cost=mana(20, pl(20, 120, 10, 60)), target="Self",
  effects=[eff("ApplyAura", aura="hunter_aspect_of_the_hawk")], tags=["Aspect"], aiHint="Buff", aiPriority=8)
U("hunter_aspect_of_the_hawk", "Aspect of the Hawk", "hawk", "Ranged attack power increased.",
  mods=[mod("RangedAttackPower", values=[20, 35, 50, 70, 90, 110, 120])], **ASPECT)

A("hunter_aspect_of_the_cheetah", "Aspect of the Cheetah", "cheetah", "Nature",
  "The hunter takes on the aspects of a cheetah, increasing movement speed by 30%. If the hunter is struck, she "
  "will be dazed for 4 sec. Only one Aspect can be active at a time.",
  learn=20, cost=mana(40), target="Self", effects=[eff("ApplyAura", aura="hunter_aspect_of_the_cheetah")],
  tags=["Aspect"], aiHint="Buff", aiPriority=0)
U("hunter_aspect_of_the_cheetah", "Aspect of the Cheetah", "cheetah", "Movement speed increased by 30%. Dazed when struck.",
  mods=[mod("MoveSpeed", 30)],
  procs=[{"trigger": "OnStruck", "chance": 100, "effects": [eff("ApplyAura", target="Self", aura="hunter_dazed")]}],
  **ASPECT)
U("hunter_dazed", "Dazed", "boot", "Movement speed reduced by 50%.", kind="Debuff", duration=4, states=["Daze"])

A("hunter_aspect_of_the_pack", "Aspect of the Pack", "paw", "Nature",
  "The hunter and party members within 20 yards take on the aspects of a pack of cheetahs, increasing movement speed "
  "by 30%. Any member struck will be dazed for 4 sec. Only one Aspect can be active at a time.",
  learn=40, cost=mana(100), target="Self", effects=[eff("ApplyAura", aura="hunter_aspect_of_the_pack")],
  tags=["Aspect"], aiHint="Buff", aiPriority=0)
U("hunter_aspect_of_the_pack", "Aspect of the Pack", "paw", "Party members within 20 yards move 30% faster.",
  radius=20, radiusAura="hunter_aspect_of_the_pack_buff", radiusAffects="Allies", **ASPECT)
U("hunter_aspect_of_the_pack_buff", "Aspect of the Pack", "paw", "Movement speed increased by 30%. Dazed when struck.",
  school="Nature", mods=[mod("MoveSpeed", 30)],
  procs=[{"trigger": "OnStruck", "chance": 100, "effects": [eff("ApplyAura", target="Self", aura="hunter_dazed")]}])

A("hunter_aspect_of_the_beast", "Aspect of the Beast", "bear", "Nature",
  "The hunter takes on the aspects of a beast, becoming untrackable and increasing the melee attack power of the "
  "hunter and her pet by 10%. Only one Aspect can be active at a time.",
  learn=30, cost=mana(50), target="Self", effects=[eff("ApplyAura", aura="hunter_aspect_of_the_beast")],
  tags=["Aspect"], aiHint="Buff", aiPriority=1)
U("hunter_aspect_of_the_beast", "Aspect of the Beast", "bear", "Melee attack power of the hunter and her pet increased by 10%.",
  mods=[mod("AttackPower", 10, pct=True), mod("AttackPower", 10, pct=True, target="Pet")], **ASPECT)

A("hunter_aspect_of_the_wild", "Aspect of the Wild", "nature", "Nature",
  "The hunter and party members within 30 yards take on the aspects of the wild, increasing Nature resistance by "
  "45 (rank 1) or 60 (rank 2). Only one Aspect can be active at a time.",
  learn=46, ranks=[46, 56], cost=mana(100, pl(100, 140, 46, 56)), target="Self",
  effects=[eff("ApplyAura", aura="hunter_aspect_of_the_wild")], tags=["Aspect"], aiHint="Buff", aiPriority=1)
U("hunter_aspect_of_the_wild", "Aspect of the Wild", "nature", "Party members within 30 yards gain Nature resistance.",
  radius=30, radiusAura="hunter_aspect_of_the_wild_buff", radiusAffects="Allies", **ASPECT)
U("hunter_aspect_of_the_wild_buff", "Aspect of the Wild", "nature", "Nature resistance increased.", school="Nature",
  mods=[mod("Resistance", values=[45, 60], school="Nature")])

# =====================================================================================================================
# Pet handling
# =====================================================================================================================
A("hunter_call_pet", "Call Pet", "wolf", "Physical",
  "Summons your pet to your side. Until you tame another beast your companion is a loyal wolf.",
  learn=1, trainCost=0, target="Self", requires={"noPet": True},
  effects=[eff("Summon", target="Self", summon="hunter_pet_wolf", lifetime=-1)],
  tags=["Pet"], special="HunterCallPet", aiHint="Summon", aiPriority=9)

A("hunter_dismiss_pet", "Dismiss Pet", "paw", "Physical",
  "Dismiss your pet. It can be called back with Call Pet.",
  learn=1, trainCost=0, castTime=5, target="Self", requires={"hasPet": True},
  effects=[eff("Special", target="Self", special="HunterDismissPet")], tags=["Pet"], aiHint="Utility", aiPriority=0)

A("hunter_revive_pet", "Revive Pet", "sparkle", "Nature",
  "Revive your pet, returning it to life with 15% of its base health.",
  learn=1, trainCost=0, castTime=10, cost={"type": "Mana", "pctBaseMana": 80}, target="Self",
  effects=[eff("Special", target="Self", special="HunterRevivePet", pctOfMax=15)],
  tags=["Pet"], aiHint="Summon", aiPriority=4)

A("hunter_mend_pet", "Mend Pet", "heart", "Nature",
  "Heals your pet for {0} health every second for 5 sec (channeled; 100 total at rank 1, 1225 at rank 7).",
  learn=12, ranks=[12, 20, 28, 36, 44, 52, 60], channeled=True, castTime=5, channelTicks=5,
  cost=mana(40, pl(40, 325, 12, 60)), target="Pet", range=20, requires={"hasPet": True},
  effects=[eff("Heal", min=20, perLevel=pl(20, 245, 12, 60))], tags=["Pet"], aiHint="Heal", aiPriority=6)

A("hunter_tame_beast", "Tame Beast", "hand", "Nature",
  "Begins taming a beast to be your companion (channeled 20 sec). The beast must be tameable and not higher level "
  "than you, and you must not have an active pet. If the channel is interrupted the taming fails.",
  learn=10, channeled=True, castTime=20, channelTicks=1, target="Enemy", range=30,
  requires={"targetCreatureTypes": ["Beast"], "noPet": True},
  effects=[eff("Special", special="HunterTameBeast")], tags=["Pet"], aiHint="Utility", aiPriority=0)

A("hunter_feign_death", "Feign Death", "skull", "Physical",
  "Feign death which may trick enemies into ignoring you: every enemy that fails to resist drops all threat on you. "
  "Lasts up to 6 min; acting or moving ends it.",
  learn=30, time="OffGcd", cooldown=30, cost=mana(80), target="Self",
  effects=[eff("Special", target="Self", special="HunterFeignDeath"),
           eff("ApplyAura", target="Self", aura="hunter_feign_death")],
  aiHint="Defensive", aiPriority=4)
U("hunter_feign_death", "Feign Death", "skull", "Feigning death. Enemies ignore you until you act or move.",
  duration=360, states=["FeignDeath"], breakOnAction=True, breakOnMove=True, special="HunterFeignDeath")

# =====================================================================================================================
# Traps: SummonTotem of a hidden, untargetable "trap" unit (totemElement "Trap" => one trap per hunter, as in Classic).
# The trap acts like a totem: at the end of the hunter's turn it uses its ability on an enemy within 5 yards; the
# HunterTrap special also springs it the moment an enemy moves within 5 yards. Its ability ends with Kill(Self).
# =====================================================================================================================
TRAP_PLACE = dict(target="Point", range=5)
A("hunter_immolation_trap", "Immolation Trap", "flame_wave", "Fire",
  "Place a fire trap that will burn the first enemy to approach for 105 (rank 1) to 690 (rank 5) Fire damage over "
  "15 sec. Trap will exist for 1 min. Only one trap can be active at a time.",
  learn=16, ranks=[16, 26, 36, 46, 56], cooldown=15, cooldownGroup="hunter_fire_trap",
  cost=mana(50, pl(50, 245, 16, 56)),
  effects=[eff("SummonTotem", target="Self", summon="hunter_trap_immolation", totemElement="Trap", lifetime=60)],
  tags=["Trap"], aiHint="Damage", aiPriority=3, **TRAP_PLACE)
A("hunter_immolation_trap_effect", "Immolation Trap", "flame_wave", "Fire",
  "The trap bursts into flame, burning the enemy for Fire damage over 15 sec.",
  learn=16, ranks=[16, 26, 36, 46, 56], hidden=True, time="OffGcd", target="Enemy", range=5,
  effects=[eff("ApplyAura", aura="hunter_immolation_trap"), eff("Kill", target="Self")],
  tags=["Trap"], aiHint="Damage", aiPriority=10)
U("hunter_immolation_trap", "Immolation Trap", "flame_wave", "Burning for Fire damage every 3 sec.", kind="Debuff",
  school="Fire", dispel="Magic", duration=15, tickInterval=3,
  tickEffects=[eff("Damage", min=21, perLevel=pl(21, 138, 16, 56))])

A("hunter_freezing_trap", "Freezing Trap", "ice_block", "Frost",
  "Place a frost trap that freezes the first enemy that approaches, preventing all action for up to 20 sec. Any "
  "damage caused will break the ice. Trap will exist for 1 min. Only one trap can be active at a time.",
  learn=20, ranks=[20, 40, 60], cooldown=15, cooldownGroup="hunter_frost_trap", cost=mana(50, pl(50, 100, 20, 60)),
  effects=[eff("SummonTotem", target="Self", summon="hunter_trap_freezing", totemElement="Trap", lifetime=60)],
  tags=["Trap"], aiHint="CC", aiPriority=4, **TRAP_PLACE)
A("hunter_freezing_trap_effect", "Freezing Trap", "ice_block", "Frost",
  "The trap freezes the enemy solid.",
  learn=20, ranks=[20, 40, 60], hidden=True, time="OffGcd", target="Enemy", range=5,
  effects=[eff("ApplyAura", aura="hunter_freezing_trap"), eff("Kill", target="Self")],
  tags=["Trap"], aiHint="CC", aiPriority=10)
U("hunter_freezing_trap", "Freezing Trap", "ice_block", "Frozen solid. Any damage breaks the ice.", kind="Debuff",
  school="Frost", dispel="Magic", duration=20, states=["Incapacitate"], breakOnDamage=True, breakDamageThreshold=0)

A("hunter_frost_trap", "Frost Trap", "frost_nova", "Frost",
  "Place a frost trap that creates an ice slick around itself for 30 sec when the first enemy approaches it. All "
  "enemies within 10 yards will be slowed by 60% while in the area of effect. Trap will exist for 1 min. Only one "
  "trap can be active at a time.",
  learn=28, cooldown=15, cooldownGroup="hunter_frost_trap", cost=mana(60),
  effects=[eff("SummonTotem", target="Self", summon="hunter_trap_frost", totemElement="Trap", lifetime=60)],
  tags=["Trap"], aiHint="CC", aiPriority=3, **TRAP_PLACE)
A("hunter_frost_trap_effect", "Frost Trap", "frost_nova", "Frost",
  "The trap shatters into an ice slick that slows enemies within 10 yards by 60% for 30 sec.",
  learn=28, hidden=True, time="OffGcd", cooldown=600, target="Enemy", range=5,
  effects=[eff("ApplyAura", target="Self", aura="hunter_frost_trap_ice")],
  tags=["Trap"], aiHint="CC", aiPriority=10)
U("hunter_frost_trap_ice", "Ice Slick", "frost_nova", "An ice slick slowing enemies within 10 yards by 60%.",
  school="Frost", duration=30, hidden=True, radius=10, radiusAura="hunter_frost_trap", radiusAffects="Enemies",
  onExpire=[eff("Kill", target="Self")])
U("hunter_frost_trap", "Frost Trap Aura", "frost_nova", "Movement speed reduced by 60%.", kind="Debuff",
  school="Frost", mods=[mod("MoveSpeed", -60)])

A("hunter_explosive_trap", "Explosive Trap", "meteor", "Fire",
  "Place a fire trap that explodes when an enemy approaches, causing 100-130 (rank 1) to 201-257 (rank 3) Fire "
  "damage plus 150 to 330 additional Fire damage over 20 sec to all enemies within 10 yards. Trap will exist for "
  "1 min. Only one trap can be active at a time.",
  learn=34, ranks=[34, 44, 54], cooldown=15, cooldownGroup="hunter_fire_trap", cost=mana(275, pl(275, 520, 34, 54)),
  effects=[eff("SummonTotem", target="Self", summon="hunter_trap_explosive", totemElement="Trap", lifetime=60)],
  tags=["Trap"], aiHint="AoE", aiPriority=4, **TRAP_PLACE)
A("hunter_explosive_trap_effect", "Explosive Trap", "meteor", "Fire",
  "The trap explodes for {0} Fire damage to enemies within 10 yards, then burns them over 20 sec.",
  learn=34, ranks=[34, 44, 54], hidden=True, time="OffGcd", target="Enemy", range=5,
  area={"shape": "Circle", "radius": 10, "affects": "Enemies"},
  effects=[eff("Damage", min=100, max=130, perLevel=pl(115, 229, 34, 54)),
           eff("ApplyAura", aura="hunter_explosive_trap"), eff("Kill", target="Self")],
  tags=["Trap"], aiHint="AoE", aiPriority=10)
U("hunter_explosive_trap", "Explosive Trap", "meteor", "Burning for Fire damage every 2 sec.", kind="Debuff",
  school="Fire", dispel="Magic", duration=20, tickInterval=2,
  tickEffects=[eff("Damage", min=15, perLevel=pl(15, 33, 34, 54))])

U("hunter_trap_armed", "Armed Trap", "trap",
  "A hidden hunter's trap. Springs when an enemy comes within 5 yards (or is within 5 yards at the end of the "
  "hunter's turn).", hidden=True, states=["Untargetable"], special="HunterTrap")


def trap_creature(cid, name, ability, desc):
    return {"id": cid, "name": name, "description": desc, "sprite": "fx_rune_circle", "type": "Totem", "rank": "Totem",
            "levelMin": 1, "levelMax": 60, "damageMult": 1, "armorMult": 0, "attackSpeed": 2, "moveSpeed": 0,
            "size": 0.5, "ai": "Totem", "totemElement": "Trap",
            "abilities": [{"ability": ability, "priority": 10}], "passives": ["hunter_trap_armed"],
            "immune": CC_IMMUNE, "xpMult": 0}


CREATURES = [
    trap_creature("hunter_trap_immolation", "Immolation Trap", "hunter_immolation_trap_effect",
                  "A hidden fire trap laid by a hunter."),
    trap_creature("hunter_trap_freezing", "Freezing Trap", "hunter_freezing_trap_effect",
                  "A hidden frost trap laid by a hunter."),
    trap_creature("hunter_trap_frost", "Frost Trap", "hunter_frost_trap_effect",
                  "A hidden frost trap laid by a hunter; becomes an ice slick when sprung."),
    trap_creature("hunter_trap_explosive", "Explosive Trap", "hunter_explosive_trap_effect",
                  "A hidden explosive trap laid by a hunter."),
]

# =====================================================================================================================
# Passives (trainer)
# =====================================================================================================================
A("hunter_parry", "Parry", "swords", "Physical", "Gives a chance to parry enemy melee attacks.", learn=8, passive=True)
A("hunter_dual_wield", "Dual Wield", "daggers", "Physical",
  "Allows one-hand and off-hand weapons to be equipped in the off hand.", learn=20, passive=True)
A("hunter_mail", "Mail", "armor", "Physical", "Allows the wearing of mail armor.", learn=40, passive=True)

# =====================================================================================================================
# Talent abilities
# =====================================================================================================================
A("hunter_aimed_shot", "Aimed Shot", "bow", "Physical",
  "An aimed shot that increases ranged damage by 70 (rank 1) to 600 (rank 6): {0}. 3 sec cast.",
  learn=20, ranks=[20, 28, 36, 44, 52, 60], fromTalent=True, castTime=3, cooldown=6,
  cost=mana(75, pl(75, 310, 20, 60)), target="Enemy", requires=RANGED,
  effects=[eff("WeaponDamage", weaponPct=100, ranged=True, min=70, perLevel=pl(70, 600, 20, 60))],
  tags=["Shot"], aiHint="Damage", aiPriority=7, **SHOT)

A("hunter_scatter_shot", "Scatter Shot", "sparkle", "Physical",
  "A short-range shot that deals 50% weapon damage ({0}) and disorients the target for 4 sec. Any damage caused "
  "will remove the effect.",
  learn=30, fromTalent=True, cooldown=30, cost=mana(80), target="Enemy", range=15, minRange=8, requires=RANGED,
  effects=[eff("WeaponDamage", weaponPct=50, ranged=True), eff("ApplyAura", aura="hunter_scatter_shot")],
  tags=["Shot"], aiHint="CC", aiPriority=5)
U("hunter_scatter_shot", "Scatter Shot", "sparkle", "Disoriented.", kind="Debuff", duration=4,
  states=["Incapacitate"], breakOnDamage=True, breakDamageThreshold=0)

A("hunter_trueshot_aura", "Trueshot Aura", "aura", "Arcane",
  "Increases the attack power and ranged attack power of party members within 45 yards by 50 (rank 1), 75 or "
  "100 (rank 3). Lasts 30 min.",
  learn=40, ranks=[40, 50, 60], fromTalent=True, cost=mana(325, pl(325, 525, 40, 60)), target="Self",
  effects=[eff("ApplyAura", aura="hunter_trueshot_aura")], tags=["Aura"], aiHint="Buff", aiPriority=8)
U("hunter_trueshot_aura", "Trueshot Aura", "aura", "Party members within 45 yards gain attack power.",
  school="Arcane", duration=1800, tags=["Aura"], radius=45, radiusAura="hunter_trueshot_aura_buff",
  radiusAffects="Allies")
U("hunter_trueshot_aura_buff", "Trueshot Aura", "aura", "Attack power and ranged attack power increased.",
  school="Arcane", mods=[mod("AttackPower", values=[50, 75, 100]), mod("RangedAttackPower", values=[50, 75, 100])])

A("hunter_intimidation", "Intimidation", "shout", "Physical",
  "Command your pet to intimidate the target on its next successful melee attack, causing a high amount of threat "
  "and stunning the target for 3 sec.",
  learn=30, fromTalent=True, cooldown=60, target="Self", requires={"hasPet": True},
  effects=[eff("ApplyAura", target="Pet", aura="hunter_intimidation")], tags=["Pet"], aiHint="CC", aiPriority=6)
U("hunter_intimidation", "Intimidation", "shout", "The next successful melee attack stuns the target for 3 sec.",
  duration=15, charges=1,
  procs=[{"trigger": "OnMeleeHit", "chance": 100, "consumeCharge": True,
          "effects": [eff("ApplyAura", target="Target", aura="hunter_intimidation_stun"),
                      eff("Threat", target="Target", threat=300)]}])
U("hunter_intimidation_stun", "Intimidation", "shout", "Stunned.", kind="Debuff", duration=3, states=["Stun"])

A("hunter_bestial_wrath", "Bestial Wrath", "rage", "Physical",
  "Send your pet into a rage causing 50% additional damage for 18 sec. While enraged, the beast does not feel pity "
  "or remorse or fear and it cannot be stopped unless killed.",
  learn=40, fromTalent=True, time="OffGcd", cooldown=120, target="Self", requires={"hasPet": True},
  effects=[eff("ApplyAura", target="Pet", aura="hunter_bestial_wrath")], tags=["Pet"], aiHint="Buff", aiPriority=7)
U("hunter_bestial_wrath", "Bestial Wrath", "rage", "Damage increased by 50%. Immune to fear, stun and crowd control.",
  duration=18, tags=["Frenzy"], mods=[mod("DamageDone", 50)], special="HunterBestialWrath")

A("hunter_deterrence", "Deterrence", "shield", "Physical",
  "When activated, increases your Dodge and Parry chance by 25% for 10 sec.",
  learn=20, fromTalent=True, cooldown=300, target="Self", effects=[eff("ApplyAura", aura="hunter_deterrence")],
  aiHint="Defensive", aiPriority=7)
U("hunter_deterrence", "Deterrence", "shield", "Dodge and Parry chance increased by 25%.", duration=10,
  mods=[mod("Dodge", 25), mod("Parry", 25)])

A("hunter_counterattack", "Counterattack", "fist", "Physical",
  "A strike that becomes active after parrying an opponent's attack. This attack deals {0} damage and immobilizes "
  "the target for 5 sec. Counterattack cannot be blocked, dodged, or parried.",
  learn=30, ranks=[30, 42, 54], fromTalent=True, cooldown=5, cost=mana(45, pl(45, 85, 30, 54)), target="Enemy",
  melee=True, requires={"reactive": "SelfParried"},
  effects=[eff("Damage", min=40, perLevel=pl(40, 110, 30, 54), cannotMiss=True),
           eff("ApplyAura", aura="hunter_counterattack")], tags=["Melee"], aiHint="Damage", aiPriority=8)
U("hunter_counterattack", "Counterattack", "fist", "Immobilized.", kind="Debuff", duration=5, states=["Root"])

A("hunter_wyvern_sting", "Wyvern Sting", "poison", "Nature",
  "A stinging shot that puts the target to sleep for 12 sec. Any damage will cancel the effect. When the target wakes "
  "up, the Sting causes 300 (rank 1) to 600 (rank 3) Nature damage over 12 sec. Only one Sting per Hunter can be "
  "active on the target at a time.",
  learn=40, ranks=[40, 50, 60], fromTalent=True, cooldown=120, cost=mana(115, pl(115, 205, 40, 60)), target="Enemy",
  requires=RANGED, effects=[eff("ApplyAura", aura="hunter_wyvern_sting")], tags=["Sting"], aiHint="CC",
  aiPriority=6, **SHOT)
U("hunter_wyvern_sting", "Wyvern Sting", "poison", "Asleep. Any damage will wake the target.", duration=12,
  states=["Sleep"], breakOnDamage=True, breakDamageThreshold=0,
  onRemove=[eff("ApplyAura", target="Target", aura="hunter_wyvern_sting_dot")],
  **{**STING, "tags": ["Sting", "Sleep"]})
U("hunter_wyvern_sting_dot", "Wyvern Sting", "poison", "Suffering Nature damage every 3 sec.", duration=12,
  tickInterval=3, tickEffects=[eff("Damage", min=75, perLevel=pl(75, 150, 40, 60))], **STING)

# Talent proc auras ----------------------------------------------------------------------------------------------------
U("hunter_quick_shots", "Quick Shots", "hawk", "Ranged attack speed increased by 30%.", school="Nature", duration=12,
  mods=[mod("RangedHaste", 30)])
U("hunter_improved_concussive_shot", "Improved Concussive Shot", "arrow", "Stunned.", kind="Debuff", duration=3,
  states=["Stun"])
U("hunter_improved_wing_clip", "Improved Wing Clip", "feather", "Immobilized.", kind="Debuff", duration=5,
  states=["Root"])
U("hunter_entrapment", "Entrapment", "trap", "Immobilized.", kind="Debuff", school="Nature", duration=5,
  states=["Root"])
U("hunter_pet_frenzy", "Frenzy", "rage", "Attack speed increased by 30%.", duration=8, mods=[mod("MeleeHaste", 30)])

# =====================================================================================================================
# Pet abilities (hidden; pets scale with their level; Focus: +36 per turn, max 100)
# =====================================================================================================================
PET = dict(hidden=True, scaleWithLevel=True)
A("hunter_pet_bite", "Bite", "fang", "Physical", "Bite the enemy, causing {0} damage.",
  learn=1, ranks=[1, 8, 16, 24, 32, 40, 48, 56], cooldown=10, cost=focus(35), target="Enemy", melee=True,
  effects=[eff("Damage", min=7, max=9, perLevel=1.14)], tags=["Pet"], aiHint="Damage", aiPriority=6, **PET)
A("hunter_pet_claw", "Claw", "claw", "Physical", "Claw the enemy, causing {0} damage.",
  learn=1, ranks=[1, 8, 16, 24, 32, 40, 48, 56], cost=focus(25), target="Enemy", melee=True,
  effects=[eff("Damage", min=4, max=6, perLevel=0.9)], tags=["Pet"], aiHint="Damage", aiPriority=5, **PET)
A("hunter_pet_growl", "Growl", "shout", "Physical",
  "Taunt the target, increasing the likelihood the creature will focus attacks on you, and generating 50 (rank 1) to "
  "415 (rank 7) threat.",
  learn=1, ranks=[1, 10, 20, 30, 40, 50, 60], cooldown=5, cost=focus(15), target="Enemy", range=5,
  effects=[eff("Taunt"), eff("Threat", threat=50, perLevel=pl(50, 415, 1, 60))], tags=["Pet", "Taunt"],
  aiHint="Taunt", aiPriority=4, **PET)
A("hunter_pet_dash", "Dash", "boot", "Physical", "Increases movement speed by 40% (level 30) to 80% (level 60) for 15 sec.",
  learn=30, ranks=[30, 40, 50], cooldown=30, cost=focus(20), target="Self",
  effects=[eff("ApplyAura", aura="hunter_pet_dash")], tags=["Pet"], aiHint="Utility", aiPriority=1, **PET)
U("hunter_pet_dash", "Dash", "boot", "Movement speed increased.", duration=15,
  mods=[mod("MoveSpeed", 40, perLevel=pl(40, 80, 30, 60))])
A("hunter_pet_dive", "Dive", "feather", "Physical", "Increases movement speed by 40% (level 30) to 80% (level 60) for 15 sec.",
  learn=30, ranks=[30, 40, 50], cooldown=30, cost=focus(20), target="Self",
  effects=[eff("ApplyAura", aura="hunter_pet_dive")], tags=["Pet"], aiHint="Utility", aiPriority=1, **PET)
U("hunter_pet_dive", "Dive", "feather", "Movement speed increased.", duration=15,
  mods=[mod("MoveSpeed", 40, perLevel=pl(40, 80, 30, 60))])
A("hunter_pet_furious_howl", "Furious Howl", "wolf", "Physical",
  "Your pet lets loose a furious howl, increasing the damage done by the next attack of party members within "
  "15 yards by 9-11 (rank 1) to 45-47 (rank 4). Lasts 10 sec.",
  learn=10, ranks=[10, 24, 40, 56], cooldown=10, cost=focus(20), target="Self",
  effects=[eff("ApplyAura", target="AlliesInRadius", radius=15, aura="hunter_pet_furious_howl")],
  tags=["Pet"], aiHint="Buff", aiPriority=4, **PET)
U("hunter_pet_furious_howl", "Furious Howl", "wolf", "The next attack deals additional damage.", duration=10,
  charges=1,
  procs=[{"trigger": "OnMeleeHit", "chance": 100, "consumeCharge": True,
          "effects": [eff("Damage", target="Target", min=9, max=11, perLevel=pl(10, 46, 10, 56))]},
         {"trigger": "OnRangedHit", "chance": 100, "consumeCharge": True,
          "effects": [eff("Damage", target="Target", min=9, max=11, perLevel=pl(10, 46, 10, 56))]}])
A("hunter_pet_prowl", "Prowl", "stealth", "Physical",
  "Places the pet into stealth mode, slowing movement by 50% (level 30) to 30% (level 60). The first attack from stealth deals "
  "20% more damage. Lasts until cancelled.",
  learn=30, ranks=[30, 40, 50], cooldown=10, target="Self", requires={"notInCombat": True},
  effects=[eff("ApplyAura", aura="hunter_pet_prowl")], tags=["Pet"], breaksStealth=False, aiHint="Utility",
  aiPriority=2, **PET)
U("hunter_pet_prowl", "Prowl", "stealth", "Stealthed. Movement slowed. Next attack deals 20% more damage.",
  duration=0, tags=["Stealth"], states=["Stealth"], breakOnAction=True,
  mods=[mod("MoveSpeed", -50, perLevel=pl(-50, -30, 30, 60)), mod("DamageDone", 20)])
A("hunter_pet_charge", "Charge", "charge", "Physical",
  "Your pet charges an enemy, immobilizing the target for 1 sec, and increasing the pet's melee attack power by "
  "50 (level 1) to 500 (level 60) for its next attack.",
  learn=1, ranks=[1, 12, 24, 36, 48, 60], cooldown=25, cost=focus(35), target="Enemy", range=25, minRange=8,
  requires={"outOfMeleeRange": True},
  effects=[eff("Charge"), eff("ApplyAura", aura="hunter_pet_charge_root"),
           eff("ApplyAura", target="Self", aura="hunter_pet_charge")],
  tags=["Pet"], aiHint="Opener", aiPriority=7, **PET)
U("hunter_pet_charge_root", "Charge", "charge", "Immobilized.", kind="Debuff", duration=1, states=["Root"])
U("hunter_pet_charge", "Charge", "charge", "Next melee attack has increased attack power.", duration=6, charges=1,
  mods=[mod("AttackPower", 50, perLevel=pl(50, 500, 1, 60))],
  procs=[{"trigger": "OnMeleeHit", "chance": 100, "consumeCharge": True, "effects": []}])
A("hunter_pet_screech", "Screech", "owl", "Physical",
  "Blasts a single enemy for {0} damage and lowers the melee attack power of all enemies within 10 yards by "
  "25 (level 8) to 100 (level 60) for 4 sec.",
  learn=8, ranks=[8, 24, 48, 56], cost=focus(20), target="Enemy", melee=True,
  effects=[eff("Damage", min=7, max=9, perLevel=pl(8, 40, 8, 56)),
           eff("ApplyAura", target="EnemiesInRadius", radius=10, aura="hunter_pet_screech")],
  tags=["Pet"], aiHint="Damage", aiPriority=5, **PET)
U("hunter_pet_screech", "Screech", "owl", "Melee attack power reduced.", kind="Debuff", duration=4,
  mods=[mod("AttackPower", -25, perLevel=pl(-25, -100, 8, 60))])


def pet(cid, name, family, sprite, size, atk, hp, dmg, arm, abilities, desc):
    return {"id": cid, "name": name, "description": desc, "sprite": sprite, "type": "Beast", "family": family,
            "rank": "Pet", "levelMin": 1, "levelMax": 60, "healthMult": hp, "damageMult": dmg, "armorMult": arm,
            "resource": "Focus", "attackSpeed": atk, "size": size, "ai": "Pet",
            "abilities": [{"ability": a, "priority": p} for a, p in abilities]}


CREATURES += [
    pet("hunter_pet_wolf", "Wolf", "Wolf", "pet_wolf", 1.1, 2.0, 1.0, 1.0, 1.05,
        [("hunter_pet_bite", 6), ("hunter_pet_furious_howl", 4), ("hunter_pet_growl", 3), ("hunter_pet_dash", 1)],
        "A loyal grey wolf. Wolves bite hard and their Furious Howl empowers the whole party."),
    pet("hunter_pet_cat", "Cat", "Cat", "pet_cat", 1.0, 1.5, 0.98, 1.1, 1.0,
        [("hunter_pet_claw", 6), ("hunter_pet_growl", 3), ("hunter_pet_prowl", 2), ("hunter_pet_dash", 1)],
        "A sleek forest cat: the hardest-hitting pet family, able to Prowl unseen."),
    pet("hunter_pet_boar", "Boar", "Boar", "pet_boar", 1.0, 2.0, 1.04, 0.9, 1.09,
        [("hunter_pet_charge", 7), ("hunter_pet_bite", 6), ("hunter_pet_growl", 4), ("hunter_pet_dash", 1)],
        "A stubborn boar with thick hide that Charges into battle."),
    pet("hunter_pet_bear", "Bear", "Bear", "pet_bear", 1.6, 2.0, 1.08, 0.91, 1.05,
        [("hunter_pet_claw", 6), ("hunter_pet_bite", 5), ("hunter_pet_growl", 4)],
        "A hulking bear: the sturdiest pet family, a natural tank."),
    pet("hunter_pet_owl", "Owl", "Owl", "pet_owl", 1.0, 2.0, 1.0, 1.07, 1.0,
        [("hunter_pet_claw", 6), ("hunter_pet_screech", 5), ("hunter_pet_growl", 3), ("hunter_pet_dive", 1)],
        "A wise owl whose Screech weakens every nearby foe."),
]

# =====================================================================================================================
# Talents
# =====================================================================================================================
BM = "hunter_bm_"
MM = "hunter_mm_"
SV = "hunter_sv_"

bm = [
    talent(BM + "improved_aspect_of_the_hawk", "Improved Aspect of the Hawk", "hawk", 1, 1, 5,
           "While Aspect of the Hawk is active, all normal ranged attacks have a {1/2/3/4/5}% chance of increasing "
           "ranged attack speed by 30% for 12 sec.",
           [proc_p({"trigger": "OnRangedHit", "abilities": ["auto_shot"],
                    "effects": [eff("ApplyAura", target="Self", aura="hunter_quick_shots",
                                    requireTargetAura="hunter_aspect_of_the_hawk")]}, values=[1, 2, 3, 4, 5])]),
    talent(BM + "endurance_training", "Endurance Training", "heart", 1, 2, 5,
           "Increases the Health of your pets by {3/6/9/12/15}% and your total health by {1/2/3/4/5}%.",
           [stat_p("Health", 3, pct=True, target="Pet"), stat_p("Health", 1, pct=True)]),
    talent(BM + "improved_eyes_of_the_beast", "Improved Eyes of the Beast", "eye", 2, 0, 2,
           "Increases the duration of your Eyes of the Beast by {30/60} sec. (Eyes of the Beast is an exploration "
           "spell not used in Lanternvale combat: this talent has no effect.)",
           [special_p("HunterImprovedEyesOfTheBeast")]),
    talent(BM + "improved_aspect_of_the_monkey", "Improved Aspect of the Monkey", "monkey", 2, 1, 5,
           "Increases the Dodge bonus of your Aspect of the Monkey by {1/2/3/4/5}%.",
           [amod("Effect", 12.5, abilities=["hunter_aspect_of_the_monkey"])]),
    talent(BM + "thick_hide", "Thick Hide", "armor", 2, 2, 3,
           "Increases the armor rating of your pets by {10/20/30}%.",
           [stat_p("Armor", 10, pct=True, target="Pet")]),
    talent(BM + "improved_revive_pet", "Improved Revive Pet", "sparkle", 2, 3, 2,
           "Revive Pet's casting time is reduced by {3/6} sec, mana cost is reduced by {20/40}%, and increases the "
           "health your pet returns with by an additional {15/30}%.",
           [amod("CastTime", -3, abilities=["hunter_revive_pet"]), amod("Cost", -20, abilities=["hunter_revive_pet"]),
            amod("Effect", 100, abilities=["hunter_revive_pet"])]),
    talent(BM + "pathfinding", "Pathfinding", "boot", 3, 0, 2,
           "Increases the speed bonus of your Aspect of the Cheetah and Aspect of the Pack by {3/6}%.",
           [amod("Effect", 10, abilities=["hunter_aspect_of_the_cheetah", "hunter_aspect_of_the_pack"])]),
    talent(BM + "bestial_swiftness", "Bestial Swiftness", "cheetah", 3, 1, 1,
           "Increases the outdoor movement speed of your pets by 30%.",
           [stat_p("MoveSpeed", 30, target="Pet")]),
    talent(BM + "unleashed_fury", "Unleashed Fury", "claw", 3, 2, 5,
           "Increases the damage done by your pets by {4/8/12/16/20}%.",
           [stat_p("DamageDone", 4, target="Pet")]),
    talent(BM + "improved_mend_pet", "Improved Mend Pet", "heart", 4, 1, 2,
           "Gives the Mend Pet ability a {15/50}% chance of cleansing 1 Curse, Disease, Magic or Poison effect from "
           "the pet each tick.",
           [special_p("HunterImprovedMendPet", values=[15, 50])]),
    talent(BM + "ferocity", "Ferocity", "fang", 4, 2, 5,
           "Increases the critical strike chance of your pets by {3/6/9/12/15}%.",
           [stat_p("MeleeCrit", 3, target="Pet")]),
    talent(BM + "spirit_bond", "Spirit Bond", "heart", 5, 0, 2,
           "While your pet is active, you and your pet will regenerate {1/2}% of total health every 10 sec.",
           [special_p("HunterSpiritBond", values=[1, 2])]),
    talent(BM + "intimidation", "Intimidation", "shout", 5, 1, 1,
           "Command your pet to intimidate the target on the next successful melee attack, causing a high amount of "
           "threat and stunning the target for 3 sec.", [grant("hunter_intimidation")]),
    talent(BM + "bestial_discipline", "Bestial Discipline", "paw", 5, 3, 2,
           "Increases the Focus regeneration of your pets by {10/20}%.",
           [stat_p("EnergyRegen", 10, target="Pet")]),
    talent(BM + "frenzy", "Frenzy", "rage", 6, 2, 5,
           "Gives your pet a {20/40/60/80/100}% chance to gain a 30% attack speed increase for 8 sec after dealing a "
           "critical strike.",
           [proc_p({"trigger": "OnMeleeCrit", "effects": [eff("ApplyAura", target="Self", aura="hunter_pet_frenzy")]},
                   values=[20, 40, 60, 80, 100], target="Pet")],
           requires=BM + "ferocity"),
    talent(BM + "bestial_wrath", "Bestial Wrath", "rage", 7, 1, 1,
           "Send your pet into a rage causing 50% additional damage for 18 sec. While enraged, the beast does not "
           "feel pity or remorse or fear and it cannot be stopped unless killed.", [grant("hunter_bestial_wrath")],
           requires=BM + "intimidation"),
]

WEAPON_SHOTS = ["auto_shot", "hunter_aimed_shot", "hunter_multi_shot", "hunter_scatter_shot"]
mm = [
    talent(MM + "improved_concussive_shot", "Improved Concussive Shot", "arrow", 1, 1, 5,
           "Gives your Concussive Shot a {4/8/12/16/20}% chance to stun the target for 3 sec.",
           [proc_p({"trigger": "OnRangedHit", "abilities": ["hunter_concussive_shot"],
                    "effects": [eff("ApplyAura", target="Target", aura="hunter_improved_concussive_shot")]},
                   values=[4, 8, 12, 16, 20])]),
    talent(MM + "efficiency", "Efficiency", "potion_blue", 1, 2, 5,
           "Reduces the Mana cost of your Shots and Stings by {2/4/6/8/10}%.",
           [amod("Cost", -2, tags=["Shot", "Sting"])]),
    talent(MM + "improved_hunters_mark", "Improved Hunter's Mark", "eye", 2, 1, 5,
           "Increases the Ranged Attack Power bonus of your Hunter's Mark spell by {3/6/9/12/15}%.",
           [amod("Effect", 3, abilities=["hunter_hunters_mark"])]),
    talent(MM + "lethal_shots", "Lethal Shots", "arrow", 2, 2, 5,
           "Increases your critical strike chance with ranged weapons by {1/2/3/4/5}%.",
           [stat_p("RangedCrit", 1)]),
    talent(MM + "aimed_shot", "Aimed Shot", "bow", 3, 0, 1,
           "An aimed shot that increases ranged damage by 70. 3 sec cast, 6 sec cooldown.", [grant("hunter_aimed_shot")]),
    talent(MM + "improved_arcane_shot", "Improved Arcane Shot", "arcane", 3, 1, 5,
           "Reduces the cooldown of your Arcane Shot by {0.2/0.4/0.6/0.8/1} sec.",
           [amod("Cooldown", -0.2, abilities=["hunter_arcane_shot"])]),
    talent(MM + "hawk_eye", "Hawk Eye", "eagle", 3, 3, 3,
           "Increases the range of your ranged weapons by {2/4/6} yards.",
           [amod("Range", 5.714, tags=["Shot", "Sting"], abilities=["auto_shot", "hunter_volley"])]),
    talent(MM + "improved_serpent_sting", "Improved Serpent Sting", "snake", 4, 1, 5,
           "Increases the damage done by your Serpent Sting by {2/4/6/8/10}%.",
           [amod("Damage", 2, abilities=["hunter_serpent_sting"])]),
    talent(MM + "mortal_shots", "Mortal Shots", "skull", 4, 2, 5,
           "Increases your ranged weapon critical strike damage bonus by {6/12/18/24/30}%.",
           [amod("CritBonus", 6, tags=["Shot"], abilities=["auto_shot"])], requires=MM + "lethal_shots"),
    talent(MM + "scatter_shot", "Scatter Shot", "sparkle", 5, 0, 1,
           "A short-range shot that deals 50% weapon damage and disorients the target for 4 sec.",
           [grant("hunter_scatter_shot")]),
    talent(MM + "barrage", "Barrage", "multishot", 5, 1, 3,
           "Increases the damage done by your Multi-Shot and Volley spells by {5/10/15}%.",
           [amod("Damage", 5, abilities=["hunter_multi_shot", "hunter_volley"])]),
    talent(MM + "improved_scorpid_sting", "Improved Scorpid Sting", "scorpion", 5, 2, 3,
           "Reduces the Stamina of targets affected by your Scorpid Sting by {10/20/30}% of the amount of Strength "
           "reduced.", [special_p("HunterImprovedScorpidSting", values=[10, 20, 30])]),
    talent(MM + "ranged_weapon_specialization", "Ranged Weapon Specialization", "gun", 6, 2, 5,
           "Increases the damage you deal with ranged weapons by {1/2/3/4/5}%.",
           [amod("Damage", 1, abilities=WEAPON_SHOTS)]),
    talent(MM + "trueshot_aura", "Trueshot Aura", "aura", 7, 1, 1,
           "Increases the attack power of party members within 45 yards by 50. Lasts 30 min.",
           [grant("hunter_trueshot_aura")]),
]

TRAP_FX = ["hunter_immolation_trap_effect", "hunter_freezing_trap_effect", "hunter_frost_trap_effect",
           "hunter_explosive_trap_effect"]
sv = [
    talent(SV + "monster_slaying", "Monster Slaying", "boar", 1, 0, 3,
           "Increases all damage caused against Beasts, Giants and Dragonkin targets by {1/2/3}% and increases "
           "critical damage caused against Beasts, Giants and Dragonkin targets by an additional {1/2/3}%.",
           [special_p("HunterCreatureSlaying", values=[1, 2, 3])]),
    talent(SV + "humanoid_slaying", "Humanoid Slaying", "skull", 1, 1, 3,
           "Increases all damage caused against Humanoid targets by {1/2/3}% and increases critical damage caused "
           "against Humanoid targets by an additional {1/2/3}%.",
           [special_p("HunterCreatureSlaying", values=[1, 2, 3])]),
    talent(SV + "deflection", "Deflection", "swords", 1, 2, 5,
           "Increases your Parry chance by {1/2/3/4/5}%.", [stat_p("Parry", 1)]),
    talent(SV + "entrapment", "Entrapment", "trap", 2, 0, 5,
           "Gives your Immolation Trap, Frost Trap, and Explosive Trap a {5/10/15/20/25}% chance to entrap the "
           "target, preventing them from moving for 5 sec.",
           [special_p("HunterEntrapment", values=[5, 10, 15, 20, 25])]),
    talent(SV + "savage_strikes", "Savage Strikes", "claw", 2, 1, 2,
           "Increases the critical strike chance of Raptor Strike and Mongoose Bite by {10/20}%.",
           [amod("CritChance", 10, abilities=["hunter_raptor_strike", "hunter_mongoose_bite"])]),
    talent(SV + "improved_wing_clip", "Improved Wing Clip", "feather", 2, 2, 5,
           "Gives your Wing Clip ability a {4/8/12/16/20}% chance to immobilize the target for 5 sec.",
           [proc_p({"trigger": "OnMeleeHit", "abilities": ["hunter_wing_clip"],
                    "effects": [eff("ApplyAura", target="Target", aura="hunter_improved_wing_clip")]},
                   values=[4, 8, 12, 16, 20])]),
    talent(SV + "clever_traps", "Clever Traps", "trap", 3, 0, 2,
           "Increases the duration of Freezing and Frost trap effects by {15/30}% and the damage of Immolation and "
           "Explosive trap effects by {15/30}%.",
           [amod("DurationPct", 15, abilities=["hunter_freezing_trap_effect", "hunter_frost_trap_effect"],
                 target="Pet"),
            amod("Damage", 15, abilities=["hunter_immolation_trap_effect", "hunter_explosive_trap_effect"],
                 target="Pet")]),
    talent(SV + "survivalist", "Survivalist", "heart", 3, 1, 5,
           "Increases total health by {2/4/6/8/10}%.", [stat_p("Health", 2, pct=True)]),
    talent(SV + "deterrence", "Deterrence", "shield", 3, 2, 1,
           "When activated, increases your Dodge and Parry chance by 25% for 10 sec.", [grant("hunter_deterrence")]),
    talent(SV + "trap_mastery", "Trap Mastery", "trap", 4, 0, 2,
           "Decreases the chance enemies will resist trap effects by {5/10}%.",
           [amod("HitChance", 5, abilities=TRAP_FX, target="Pet")]),
    talent(SV + "surefooted", "Surefooted", "boot", 4, 1, 3,
           "Increases hit chance by {1/2/3}% and increases the chance movement impairing effects will be resisted by "
           "an additional {5/10/15}%.",
           [stat_p("MeleeHit", 1), stat_p("RangedHit", 1), special_p("HunterSurefooted", values=[5, 10, 15])]),
    talent(SV + "improved_feign_death", "Improved Feign Death", "skull", 4, 3, 2,
           "Reduces the chance your Feign Death ability will be resisted by {2/4}%.",
           [amod("HitChance", 2, abilities=["hunter_feign_death"])]),
    talent(SV + "killer_instinct", "Killer Instinct", "fang", 5, 1, 3,
           "Increases your critical strike chance with all attacks by {1/2/3}%.",
           [stat_p("MeleeCrit", 1), stat_p("RangedCrit", 1)]),
    talent(SV + "counterattack", "Counterattack", "fist", 5, 2, 1,
           "A strike that becomes active after parrying an opponent's attack. This attack deals 40 damage and "
           "immobilizes the target for 5 sec. Counterattack cannot be blocked, dodged, or parried.",
           [grant("hunter_counterattack")], requires=SV + "deterrence"),
    talent(SV + "lightning_reflexes", "Lightning Reflexes", "lightning", 6, 2, 5,
           "Increases your Agility by {3/6/9/12/15}%.", [stat_p("Agility", 3, pct=True)]),
    talent(SV + "wyvern_sting", "Wyvern Sting", "poison", 7, 1, 1,
           "A stinging shot that puts the target to sleep for 12 sec. Any damage will cancel the effect. When the "
           "target wakes up, the Sting causes 300 Nature damage over 12 sec.", [grant("hunter_wyvern_sting")],
           requires=SV + "killer_instinct"),
]

TREES = [
    {"id": "tree_hunter_beast_mastery", "classId": "Hunter", "name": "Beast Mastery", "icon": "paw",
     "description": "Master of the wild: a stronger, tougher pet and the fury of Bestial Wrath.", "talents": bm},
    {"id": "tree_hunter_marksmanship", "classId": "Hunter", "name": "Marksmanship", "icon": "bow",
     "description": "Deadly precision with bow and gun: Aimed Shot, Trueshot Aura and lethal critical strikes.",
     "talents": mm},
    {"id": "tree_hunter_survival", "classId": "Hunter", "name": "Survival", "icon": "trap",
     "description": "Traps, melee tricks and self-preservation: Deterrence, Counterattack and Wyvern Sting.",
     "talents": sv},
]

BUILD = ([BM + "improved_aspect_of_the_hawk"] * 5 + [BM + "thick_hide"] * 3 + [BM + "endurance_training"] * 2 +
         [BM + "unleashed_fury"] * 5 + [BM + "ferocity"] * 5 + [BM + "intimidation"] + [BM + "spirit_bond"] * 2 +
         [BM + "bestial_discipline"] * 2 + [BM + "frenzy"] * 5 + [BM + "bestial_wrath"] +
         [MM + "efficiency"] * 5 + [MM + "lethal_shots"] * 5 + [MM + "aimed_shot"] + [MM + "improved_arcane_shot"] * 4 +
         [MM + "mortal_shots"] * 5)

# =====================================================================================================================
# Items
# =====================================================================================================================
ITEMS = [
    {"id": "hunter_starter_worn_axe", "name": "Worn Axe", "icon": "axe",
     "description": "A notched hatchet, good for kindling and for close calls.", "kind": "Weapon",
     "quality": "Common", "itemLevel": 2, "requiredLevel": 1, "equip": "OneHand", "weaponType": "OneHandAxe",
     "minDamage": 2, "maxDamage": 5, "speed": 2.2, "price": 9},
    {"id": "hunter_starter_worn_dagger", "name": "Worn Dagger", "icon": "dagger",
     "description": "A skinning knife that has seen better days.", "kind": "Weapon", "quality": "Common",
     "itemLevel": 2, "requiredLevel": 1, "equip": "OneHand", "weaponType": "Dagger", "minDamage": 1,
     "maxDamage": 3, "speed": 1.6, "price": 7},
    {"id": "hunter_starter_worn_shortbow", "name": "Worn Shortbow", "icon": "bow",
     "description": "A supple yew shortbow strung with gut.", "kind": "Weapon", "quality": "Common",
     "itemLevel": 2, "requiredLevel": 1, "equip": "Ranged", "weaponType": "Bow", "minDamage": 2, "maxDamage": 4,
     "speed": 2.3, "price": 9},
    {"id": "hunter_starter_old_blunderbuss", "name": "Old Blunderbuss", "icon": "gun",
     "description": "Loud, smoky and surprisingly reliable.", "kind": "Weapon", "quality": "Common", "itemLevel": 2,
     "requiredLevel": 1, "equip": "Ranged", "weaponType": "Gun", "minDamage": 2, "maxDamage": 5, "speed": 2.5,
     "price": 9},
    {"id": "hunter_starter_trapper_vest", "name": "Rugged Trapper's Vest", "icon": "armor",
     "description": "Patched leather that smells faintly of pine smoke.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "requiredLevel": 1, "equip": "Chest", "armorType": "Leather", "armor": 12, "price": 5},
    {"id": "hunter_starter_trapper_pants", "name": "Rugged Trapper's Pants", "icon": "armor",
     "description": "Hard-wearing leggings with many pockets.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "requiredLevel": 1, "equip": "Legs", "armorType": "Leather", "armor": 10, "price": 5},
    {"id": "hunter_starter_trapper_boots", "name": "Rugged Trapper's Boots", "icon": "boot",
     "description": "Soft-soled boots for stalking quietly.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "requiredLevel": 1, "equip": "Feet", "armorType": "Leather", "armor": 7, "price": 4},
]

# =====================================================================================================================
# Class
# =====================================================================================================================
CLASS = {
    "id": "Hunter", "name": "Hunter",
    "description": "A ranged marksman who fights alongside a loyal beast, tames wild creatures and lays cunning traps.",
    "roles": ["Ranged DPS"], "color": "#ABD473", "icon": "bow", "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth", "Leather"], "armorUpgrade": {"level": 40, "type": "Mail"},
    "weaponTypes": ["Bow", "Gun", "Crossbow", "OneHandAxe", "TwoHandAxe", "OneHandSword", "TwoHandSword", "Polearm",
                    "Staff", "Dagger", "FistWeapon", "Thrown"],
    "dualWieldLevel": 20, "canParry": True, "canBlock": False,
    "baseStatsLevel1": {"strength": 20, "agility": 23, "stamina": 21, "intellect": 20, "spirit": 22},
    "baseStatsLevel60": {"strength": 55, "agility": 125, "stamina": 90, "intellect": 65, "spirit": 71},
    "baseHealthLevel1": 46, "baseHealthLevel60": 1467, "baseManaLevel1": 65, "baseManaLevel60": 1720,
    "meleeAp": "StrengthPlusAgility", "rangedApAgilityTimesTwo": True,
    "agilityPerMeleeCritAt60": 53, "intellectPerSpellCritAt60": 60, "agilityPerDodgeAt60": 26.5,
    "baseMeleeCrit": 5, "baseSpellCrit": 5, "baseDodge": 5,
    "manaRegenBase": 15, "manaRegenPerSpirit": 0.2,
    "basicAttack": "auto_shot",
    "startingAbilities": ["auto_shot", "attack", "hunter_raptor_strike", "hunter_call_pet", "hunter_dismiss_pet",
                          "hunter_revive_pet"],
    "startingItems": ["hunter_starter_worn_axe", "hunter_starter_worn_shortbow", "hunter_starter_trapper_vest",
                      "hunter_starter_trapper_pants", "hunter_starter_trapper_boots"],
    "startingStance": "",
    "talentTrees": [t["id"] for t in TREES],
    "defaultBuild": BUILD,
    "sprite": "char_hunter", "portrait": "portrait_hunter",
    "designNotes": "The Hunter fights from range with Auto Shot, Shots and Stings while a loyal beast takes its own turn "
                   "right after yours (you start with a wolf; Tame Beast recruits others). Keep foes outside your "
                   "8-yard dead zone with Wing Clip, Concussive Shot, traps and your pet, and pick one Aspect to suit "
                   "the fight. Mana is precious: Auto Shot is free, and Feign Death wipes your threat when a fight "
                   "turns against you.",
}

# =====================================================================================================================
# Specials
# =====================================================================================================================
SPECIALS = [
    {"id": "HunterCallPet", "usedBy": "hunter_call_pet",
     "behaviour": "Replaces the generic Summon of this ability. Summons the hunter's ACTIVE pet next to the hunter as a "
                  "persistent pet (Kind Pet, lifetime -1, own turn right after the hunter, resource Focus starting at "
                  "100). The active pet is stored on the hunter (save data): creature template id (default "
                  "'hunter_pet_wolf', the Summon effect's creature, until Tame Beast replaces it) plus the tamed "
                  "beast's display name. Pet level = hunter level. Fails with 'Your pet is dead' if the active pet died "
                  "and has not been revived (use Revive Pet). Requires noPet."},
    {"id": "HunterDismissPet", "usedBy": "hunter_dismiss_pet",
     "behaviour": "Removes the hunter's living pet from the battle/world without killing it (RemoveUnit reason "
                  "'dismissed'); its current health is remembered and it can be summoned again with Call Pet."},
    {"id": "HunterRevivePet", "usedBy": "hunter_revive_pet",
     "behaviour": "If the hunter's active pet is dead (killed in this or an earlier battle), returns it to life next to "
                  "the hunter with pctOfMax (15) % of its maximum health, multiplied by (1 + Effect AbilityMod%: "
                  "Improved Revive Pet +100/200% => 30/45%), and 0 Focus, and it joins the turn order after the "
                  "hunter. If the pet is alive but dismissed, it is summoned at its remembered health. No effect "
                  "(ability not usable) when the pet is alive and present."},
    {"id": "HunterTameBeast", "usedBy": "hunter_tame_beast",
     "behaviour": "Resolves when the 20 s channel completes uninterrupted (it is cancelled like any channel by stun, "
                  "fear, interrupt, death, or the hunter moving). Target must be a living hostile creature with "
                  "CreatureDef.tameable = true, type Beast, level <= hunter level, and not a Boss/Elite unless a "
                  "future flag allows it; the hunter must have no pet out. On success the creature leaves the hostile "
                  "side (RemoveUnit, no XP/loot) and becomes the hunter's active pet: template = "
                  "'hunter_pet_' + family.lower() if such a creature exists (wolf, cat, boar, bear, owl), else "
                  "'hunter_pet_wolf'; display name = the creature's name; it is summoned immediately at the creature's "
                  "position with the creature's current health % and full Focus, at the hunter's level. The previous "
                  "active pet is released. While channelling, the beast keeps attacking the hunter normally."},
    {"id": "HunterFeignDeath", "usedBy": "hunter_feign_death (Special effect + aura special)",
     "behaviour": "Two phases around the engine's generic FeignDeath handling (applying a FeignDeath-state aura zeroes "
                  "every enemy's threat on the bearer). (1) Special effect, resolved BEFORE the ApplyAura effect: for "
                  "each hostile unit with the hunter on its threat table roll a spell hit check (hunter level vs "
                  "enemy level, + HitChance AbilityMods: Improved Feign Death +2/4%); record the enemies that RESIST "
                  "and their current threat on the hunter (store on the hunter, e.g. aura Vars / a list). (2) Aura "
                  "special, OnAuraApplied of hunter_feign_death (after the generic wipe): restore the recorded threat "
                  "of resisting enemies and let them keep the hunter as their target (they may attack her although she "
                  "is feigning). Also on application: stop the hunter's auto attack, clear her queued swing, cancel "
                  "her pending cast and end her turn. Non-resisting enemies re-pick targets and ignore her while the "
                  "aura lasts (breaks on action or movement)."},
    {"id": "HunterTrap", "usedBy": "hunter_trap_armed (passive of hunter_trap_immolation/freezing/frost/explosive)",
     "behaviour": "Trap units are SummonTotem units with totemElement 'Trap' (so a hunter has one trap at a time; a "
                  "new trap replaces the old). This aura makes the trap invisible to enemy AI (not targetable, "
                  "ignored by pathing/aggro) and adds an immediate trigger: whenever a hostile unit's movement brings "
                  "it within 5 yards of the trap, stop that unit's movement at that point and make the trap use its "
                  "first creature ability on that unit at once (the unit then continues its turn if not "
                  "incapacitated). Otherwise the trap behaves like a totem: at the end of the owner's turn it uses "
                  "its ability on a hostile unit within 5 yards if any. Every trap ability ends by killing the trap "
                  "(Kill Self) except Frost Trap, which instead becomes a 30 s ice slick (its 600 s cooldown stops re-triggers). "
                  "Trap abilities use owner-'Pet' AbilityMods (Clever Traps, Trap Mastery) like totem abilities; "
                  "threat caused by a trap is credited to the owning hunter and the trap never appears on threat "
                  "tables. Traps cannot be damaged or affected by hostile abilities/auras (area effects ignore them); only their "
                  "own Kill(Self), lifetime expiry, replacement by a new trap or the end of battle remove them."},
    {"id": "HunterMark", "usedBy": "hunter_hunters_mark (aura)",
     "behaviour": "While a unit bears this aura, every ranged attack against it (Auto Shot and any ability whose hit "
                  "table is Ranged, from any attacker) is computed with +X ranged attack power, X = 20/45/75/110 for "
                  "Hunter's Mark rank 1/2/3/4 (aura Rank), multiplied by the aura's EffectMult (Improved Hunter's Mark "
                  "+3% per rank). The marked unit cannot benefit from Stealth/Invisible against the caster's team "
                  "(it stays visible and targetable)."},
    {"id": "HunterBestialWrath", "usedBy": "hunter_bestial_wrath (aura on the pet)",
     "behaviour": "While active the pet is immune to and cleansed of (on application) every aura imposing Fear, Stun, "
                  "Root, Incapacitate, Sleep, Polymorph, Confuse, Daze or a negative MoveSpeed mod; new such auras "
                  "fail to apply ('Immune'). Taunts still work. The +50% damage is the aura's DamageDone mod."},
    {"id": "HunterImprovedMendPet", "usedBy": BM + "improved_mend_pet",
     "behaviour": "Each tick of the hunter's Mend Pet channel has values[rank]% (15/50) chance to remove one random "
                  "Curse, Disease, Magic or Poison debuff from the pet."},
    {"id": "HunterSpiritBond", "usedBy": BM + "spirit_bond",
     "behaviour": "While the hunter's pet is alive and summoned: at the start of the hunter's turn the hunter heals "
                  "values[rank] x 0.6 % of her maximum health (1/2% per 10 s => 0.6/1.2% per 6 s turn) and at the "
                  "start of the pet's turn the pet heals the same percentage of its maximum health. Out of combat, "
                  "apply it per real 10 s. These heals cause no threat."},
    {"id": "HunterCreatureSlaying", "usedBy": SV + "monster_slaying, " + SV + "humanoid_slaying",
     "behaviour": "Monster Slaying: all damage the hunter deals to targets of creature type Beast, Giant or Dragonkin is "
                  "increased by values[rank]% (1/2/3) and her critical strike damage bonus against them by an "
                  "additional values[rank]% (bonus 100% -> 101/102/103%). Humanoid Slaying: the same against "
                  "Humanoid targets. Player characters/companions count as Humanoid."},
    {"id": "HunterImprovedScorpidSting", "usedBy": MM + "improved_scorpid_sting",
     "behaviour": "When the hunter's hunter_scorpid_sting aura is applied, it also reduces the target's Stamina by "
                  "values[rank]% (10/20/30) of that aura's Strength reduction (e.g. rank 4 sting -60 Str with 3/3 "
                  "talent -> -18 Stamina), for the aura's duration."},
    {"id": "HunterSurefooted", "usedBy": SV + "surefooted",
     "behaviour": "When an aura imposing Root or a negative MoveSpeed mod (snare/daze) would be applied to the hunter "
                  "by a hostile unit, roll values[rank]% (5/10/15): on success it is resisted. (The +1/2/3% hit is "
                  "the talent's Stat passives.)"},
    {"id": "HunterEntrapment", "usedBy": SV + "entrapment",
     "behaviour": "Each hostile unit damaged by the hunter's Immolation Trap or Explosive Trap effect "
                  "(hunter_immolation_trap_effect / hunter_explosive_trap_effect), and each hostile unit when it first "
                  "receives the Frost Trap slow (hunter_frost_trap aura from the ice slick), rolls values[rank]% "
                  "(5/10/15/20/25); on success apply hunter_entrapment (Root, 5 s) to it."},
    {"id": "HunterImprovedEyesOfTheBeast", "usedBy": BM + "improved_eyes_of_the_beast",
     "behaviour": "No effect. Eyes of the Beast (pet scouting) is not part of Lanternvale; the talent is kept so the "
                  "Beast Mastery tree layout and point requirements match WoW Classic 1.12. Implement as a no-op."},
]

bundle = {
    "_note": "Hunter (WoW Classic 1.12). Conventions: magnitudes are rank-1 numbers with perLevel landing the last "
             "rank on the real max rank; aura mod 'values' are indexed by the rank of the ability/talent that applied "
             "the aura; pet abilities scale with pet level (scaleWithLevel) and their auras use perLevel; channel "
             "effects are per tick; non-scalable per-rank values (durations) use the max-rank value. Traps are hidden "
             "totem-like units (totemElement 'Trap') that use their ability when an enemy comes within 5 yards and then "
             "kill themselves; owner talents reach them via AbilityMods with target 'Pet'. Abilities tagged 'Shot' "
             "or requiring a ranged weapon use the ranged hit table (crit x2), whatever their school.",
    "classes": [CLASS],
    "abilities": B.abilities,
    "auras": B.auras,
    "talentTrees": TREES,
    "items": ITEMS,
    "creatures": CREATURES,
    "specials": SPECIALS,
}

if __name__ == "__main__":
    spent = check_build(TREES, BUILD)
    write_bundle(OUT, bundle)
    trainable = [a for a in B.abilities if not a.get("hidden") and not a.get("fromTalent")]
    print("hunter: abilities", len(B.abilities), "(trainable", len(trainable), ") auras", len(B.auras),
          "talents", [len(t["talents"]) for t in TREES], "items", len(ITEMS), "creatures", len(CREATURES),
          "specials", len(SPECIALS), "build", spent)
