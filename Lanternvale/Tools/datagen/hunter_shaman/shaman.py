"""Generates Lanternvale/Assets/Lanternvale/Resources/Data/classes/shaman.json (WoW Classic 1.12 Shaman)."""
from common import *

OUT = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data/classes/shaman.json"
B = Builder("Shaman")
A, U = B.ability, B.aura

SHOCK = dict(cooldown=6, cooldownGroup="shaman_shock", target="Enemy", range=20)
IMBUE = dict(school="Nature", duration=300, exclusiveGroup="shaman_imbue", tags=["Imbue"])
CC_IMMUNE = ["Stun", "Root", "Silence", "Pacify", "Disarm", "Fear", "Polymorph", "Incapacitate", "Sleep", "Confuse", "Daze"]
DMG_SPELLS = ["shaman_lightning_bolt", "shaman_chain_lightning", "shaman_earth_shock", "shaman_flame_shock",
              "shaman_frost_shock"]
LIGHTNING = ["shaman_lightning_bolt", "shaman_chain_lightning"]

# =====================================================================================================================
# Elemental spells
# =====================================================================================================================
A("shaman_lightning_bolt", "Lightning Bolt", "lightning", "Nature",
  "Casts a bolt of lightning at the target for {0} Nature damage.",
  learn=1, ranks=[1, 8, 14, 20, 26, 32, 38, 44, 50, 56], trainCost=10, castTime=3,
  cost=mana(15, pl(15, 265, 1, 56)), target="Enemy", range=30,
  effects=[eff("Damage", min=15, max=17, perLevel=pl(16, 452.5, 1, 56), coef=0.857)],
  tags=["Lightning"], aiHint="Damage", aiPriority=5)

A("shaman_chain_lightning", "Chain Lightning", "chain_lightning", "Nature",
  "Hurls a lightning bolt at the enemy, dealing {0} Nature damage and then jumping to additional nearby enemies. "
  "Each jump reduces the damage by 30%. Affects 3 total targets.",
  learn=32, ranks=[32, 40, 48, 56], castTime=2.5, cooldown=6, cost=mana(280, pl(280, 605, 32, 56)), target="Enemy",
  range=30,
  effects=[eff("Damage", min=200, max=227, perLevel=pl(213.5, 534.5, 32, 56), coef=0.714, chainTargets=2,
               chainFalloffPct=30, chainRange=12)],
  tags=["Lightning"], aiHint="AoE", aiPriority=6)

A("shaman_earth_shock", "Earth Shock", "earth", "Nature",
  "Instantly shocks the target with concussive force, causing {0} Nature damage. It also interrupts spellcasting "
  "and prevents any spell in that school from being cast for 2 sec. Shocks share a 6 sec cooldown.",
  learn=4, ranks=[4, 8, 14, 24, 36, 48, 60], cost=mana(30, pl(30, 450, 4, 60)),
  effects=[eff("Damage", min=19, max=22, perLevel=pl(20.5, 531, 4, 60), coef=0.386), eff("Interrupt", lockout=2)],
  tags=["Shock", "Interrupt"], aiHint="Interrupt", aiPriority=8, **SHOCK)

A("shaman_flame_shock", "Flame Shock", "flame_wave", "Fire",
  "Instantly sears the target with fire, causing {0} Fire damage immediately and 28 (rank 1) to 320 (rank 6) Fire "
  "damage over 12 sec. Shocks share a 6 sec cooldown.",
  learn=10, ranks=[10, 18, 28, 40, 52, 60], cost=mana(55, pl(55, 410, 10, 60)),
  effects=[eff("Damage", min=25, perLevel=pl(25, 292, 10, 60), coef=0.15), eff("ApplyAura", aura="shaman_flame_shock")],
  tags=["Shock"], aiHint="Damage", aiPriority=6, **SHOCK)
U("shaman_flame_shock", "Flame Shock", "flame_wave", "Burning for Fire damage every 3 sec.", kind="Debuff",
  school="Fire", dispel="Magic", duration=12, tickInterval=3,
  tickEffects=[eff("Damage", min=7, perLevel=pl(7, 80, 10, 60), coef=0.1)])

A("shaman_frost_shock", "Frost Shock", "frost", "Frost",
  "Instantly shocks the target with frost, causing {0} Frost damage and slowing movement speed by 50% for 8 sec. "
  "Shocks share a 6 sec cooldown.",
  learn=20, ranks=[20, 34, 46, 58], cost=mana(115, pl(115, 430, 20, 58)),
  effects=[eff("Damage", min=95, max=101, perLevel=pl(98, 506, 20, 58), coef=0.386),
           eff("ApplyAura", aura="shaman_frost_shock")],
  tags=["Shock"], aiHint="Damage", aiPriority=5, **SHOCK)
U("shaman_frost_shock", "Frost Shock", "frost", "Movement speed reduced by 50%.", kind="Debuff", school="Frost",
  dispel="Magic", duration=8, mods=[mod("MoveSpeed", -50)])

A("shaman_lightning_shield", "Lightning Shield", "storm", "Nature",
  "The caster is surrounded by 3 balls of lightning. When a melee or ranged attack hits the caster, the attacker will "
  "be struck by lightning for 13 (rank 1) to 198 (rank 7) Nature damage. This expends one lightning ball. "
  "Lasts 10 min.",
  learn=8, ranks=[8, 16, 24, 32, 40, 48, 56], cost=mana(45, pl(45, 370, 8, 56)), target="Self",
  effects=[eff("ApplyAura", aura="shaman_lightning_shield")], tags=["Lightning"], aiHint="Buff", aiPriority=3)
U("shaman_lightning_shield", "Lightning Shield", "storm", "Attackers are struck by lightning.", school="Nature",
  dispel="Magic", duration=600, charges=3, tags=["Shield"],
  procs=[{"trigger": "OnStruck", "chance": 100, "consumeCharge": True,
          "effects": [eff("Damage", target="Attacker", min=13, perLevel=pl(13, 198, 8, 56), coef=0.267)]}])

A("shaman_purge", "Purge", "hand", "Nature",
  "Purges the enemy target, removing 2 beneficial magic effects (rank 1 removes 1 in WoW; Lanternvale uses the "
  "max-rank count).",
  learn=12, ranks=[12, 32], cost=mana(90, pl(90, 180, 12, 32)), target="Enemy", range=30,
  effects=[eff("Dispel", dispelType="Magic", dispelCount=2)], aiHint="Debuff", aiPriority=4)

# =====================================================================================================================
# Healing & utility
# =====================================================================================================================
A("shaman_healing_wave", "Healing Wave", "wave", "Nature",
  "Heals a friendly target for {0}.",
  learn=1, ranks=[1, 6, 12, 18, 24, 32, 40, 48, 56, 60], trainCost=10, castTime=3, cost=mana(25, pl(25, 620, 1, 60)),
  target="Ally", range=40,
  effects=[eff("Heal", min=34, max=44, perLevel=pl(39, 1735, 1, 60), coef=0.857)],
  tags=["Heal"], aiHint="Heal", aiPriority=6)

A("shaman_lesser_healing_wave", "Lesser Healing Wave", "water_drop", "Nature",
  "Heals a friendly target for {0}.",
  learn=20, ranks=[20, 28, 36, 44, 52, 60], castTime=1.5, cost=mana(105, pl(105, 380, 20, 60)), target="Ally",
  range=40, effects=[eff("Heal", min=162, max=186, perLevel=pl(174, 880, 20, 60), coef=0.429)],
  tags=["Heal"], aiHint="Heal", aiPriority=7)

A("shaman_chain_heal", "Chain Heal", "heal_plus", "Nature",
  "Heals the friendly target for {0}, then jumps to heal additional nearby injured allies. Each jump reduces the "
  "effectiveness of the heal by 50%. Heals 3 total targets.",
  learn=40, ranks=[40, 46, 54], castTime=2.5, cost=mana(260, pl(260, 405, 40, 54)), target="Ally", range=40,
  effects=[eff("Heal", min=332, max=381, perLevel=pl(356.5, 606.5, 40, 54), coef=0.714, chainTargets=2,
               chainFalloffPct=50, chainRange=12)],
  tags=["Heal"], aiHint="Heal", aiPriority=6)

A("shaman_cure_poison", "Cure Poison", "vial", "Nature", "Cures 1 poison effect on the target.",
  learn=16, cost=mana(50), target="Ally", range=30, effects=[eff("Dispel", dispelType="Poison", dispelCount=1)],
  tags=["Cure"], aiHint="Utility", aiPriority=5)

A("shaman_cure_disease", "Cure Disease", "cross", "Nature", "Cures 1 disease effect on the target.",
  learn=22, cost=mana(70), target="Ally", range=30, effects=[eff("Dispel", dispelType="Disease", dispelCount=1)],
  tags=["Cure"], aiHint="Utility", aiPriority=5)

A("shaman_ghost_wolf", "Ghost Wolf", "wolf_spirit", "Nature",
  "Turns the Shaman into a Ghost Wolf, increasing speed by 40%. While in this form the Shaman can only move and "
  "shapeshift. Use again (or cancel the aura) to return to normal form.",
  learn=20, castTime=3, cost=mana(55), target="Self", effects=[eff("ApplyAura", aura="shaman_ghost_wolf")],
  tags=["Shapeshift"], aiHint="Utility", aiPriority=0)
U("shaman_ghost_wolf", "Ghost Wolf", "wolf_spirit", "Movement speed increased by 40%. Shapeshifted.", school="Nature",
  duration=0, exclusiveGroup="shaman_form", tags=["Shapeshift", "Form"], states=["Shapeshift"],
  mods=[mod("MoveSpeed", 40)])

A("shaman_ancestral_spirit", "Ancestral Spirit", "wings", "Nature",
  "Returns the spirit to the body, restoring a dead or downed target to life with 25% health and mana.",
  learn=12, ranks=[12, 24, 36, 48, 60], castTime=10, cost=mana(85, pl(85, 545, 12, 60)), target="DeadAlly",
  range=30, effects=[eff("Resurrect", pctOfMax=25)], aiHint="Heal", aiPriority=3)

A("shaman_reincarnation", "Reincarnation", "halo", "Nature",
  "Allows you to resurrect yourself upon death with 20% health and mana. Consumes an Ankh. 60 min cooldown.",
  learn=30, passive=True, passiveAura="shaman_reincarnation", cooldown=3600)
U("shaman_reincarnation", "Reincarnation", "halo", "You may reincarnate when slain (requires an Ankh).",
  school="Nature", hidden=True, persistThroughDeath=True, special="ShamanReincarnation")

A("shaman_mail", "Mail", "armor", "Physical", "Allows the wearing of mail armor.", learn=40, passive=True)

# =====================================================================================================================
# Weapon imbues (self buffs, one at a time)
# =====================================================================================================================
A("shaman_rockbiter_weapon", "Rockbiter Weapon", "rock", "Nature",
  "Imbue the Shaman's weapon with stone, increasing melee attack power by 29 (rank 1) to 104 (rank 7). Lasts 5 min. "
  "Only one weapon imbue can be active.",
  learn=1, ranks=[1, 8, 16, 24, 34, 44, 54], trainCost=10, cost=mana(15, pl(15, 90, 1, 54)), target="Self",
  requires={"meleeWeapon": True}, effects=[eff("ApplyAura", aura="shaman_rockbiter_weapon")], tags=["Imbue"],
  aiHint="Buff", aiPriority=4)
U("shaman_rockbiter_weapon", "Rockbiter Weapon", "rock", "Melee attack power increased.",
  mods=[mod("AttackPower", values=[29, 39, 50, 62, 76, 90, 104])], **IMBUE)

A("shaman_flametongue_weapon", "Flametongue Weapon", "ember", "Fire",
  "Imbue the Shaman's weapon with fire. Each hit causes 9-13 (rank 1) to about 55-60 (rank 6) additional Fire "
  "damage. Lasts 5 min. Only one weapon imbue can be active.",
  learn=10, ranks=[10, 18, 26, 36, 46, 56], cost=mana(45, pl(45, 140, 10, 56)), target="Self",
  requires={"meleeWeapon": True}, effects=[eff("ApplyAura", aura="shaman_flametongue_weapon")], tags=["Imbue"],
  aiHint="Buff", aiPriority=4)
U("shaman_flametongue_weapon", "Flametongue Weapon", "ember", "Melee hits deal additional Fire damage.",
  procs=[{"trigger": "OnMeleeHit", "chance": 100,
          "effects": [eff("Damage", target="Target", school="Fire", min=9, max=13, perLevel=pl(11, 58, 10, 56),
                          coef=0.1)]}],
  **{**IMBUE, "school": "Fire"})

A("shaman_frostbrand_weapon", "Frostbrand Weapon", "ice_shard", "Frost",
  "Imbue the Shaman's weapon with frost. Each hit has a chance (9 per minute) of causing 46 (rank 1) to 156 "
  "(rank 5) additional Frost damage and slowing the target's movement speed by 50% for 8 sec. Lasts 5 min. Only one "
  "weapon imbue can be active.",
  learn=20, ranks=[20, 28, 38, 48, 58], cost=mana(70, pl(70, 145, 20, 58)), target="Self",
  requires={"meleeWeapon": True}, effects=[eff("ApplyAura", aura="shaman_frostbrand_weapon")], tags=["Imbue"],
  aiHint="Buff", aiPriority=4)
U("shaman_frostbrand_weapon", "Frostbrand Weapon", "ice_shard", "Melee hits may deal Frost damage and slow.",
  procs=[{"trigger": "OnMeleeHit", "ppm": 9,
          "effects": [eff("Damage", target="Target", school="Frost", min=46, perLevel=pl(46, 156, 20, 58), coef=0.1),
                      eff("ApplyAura", target="Target", aura="shaman_frostbrand_attack")]}],
  **{**IMBUE, "school": "Frost"})
U("shaman_frostbrand_attack", "Frostbrand Attack", "ice_shard", "Movement speed reduced by 50%.", kind="Debuff",
  school="Frost", dispel="Magic", duration=8, mods=[mod("MoveSpeed", -50)])

A("shaman_windfury_weapon", "Windfury Weapon", "wind", "Nature",
  "Imbue the Shaman's weapon with wind. Each hit has a 20% chance of dealing additional damage equal to two extra "
  "attacks with 46 (rank 1) to 333 (rank 4) extra attack power. Lasts 5 min. Only one weapon imbue can be active.",
  learn=30, ranks=[30, 40, 50, 60], cost=mana(95, pl(95, 175, 30, 60)), target="Self", requires={"meleeWeapon": True},
  effects=[eff("ApplyAura", aura="shaman_windfury_weapon")], tags=["Imbue"], aiHint="Buff", aiPriority=5)
U("shaman_windfury_weapon", "Windfury Weapon", "wind", "Melee hits have a 20% chance to grant two extra attacks.",
  procs=[{"trigger": "OnMeleeHit", "chance": 20,
          "effects": [eff("Special", target="Self", special="ShamanWindfuryAttack", count=2, min=46,
                          perLevel=pl(46, 333, 30, 60))]}],
  **IMBUE)

# =====================================================================================================================
# Totems. Each totem ability: SummonTotem at the shaman's feet (one per element). Totem creatures are stationary,
# act at the end of the owner's turn (their abilities) and/or carry area auras (their passives).
# =====================================================================================================================
CREATURES = []
ELEMENT_SCHOOL = {"Earth": "Nature", "Fire": "Fire", "Water": "Nature", "Air": "Nature"}
PULSE_BUFF_DURATION = 10  # refreshed every owner turn; lingers ~1 turn after leaving range (like WoW totem buffs)


def totem(aid, name, element, learn, ranks, cost, cost_max, lifetime, desc, cdesc, passives=(), abilities=(),
          cooldown=None, ai=("Buff", 3), health=1.0, from_talent=False, school=None):
    cid = "shaman_totem_" + aid[len("shaman_"):-len("_totem")]
    last = ranks[-1] if ranks else learn
    A(aid, name, "totem_" + element.lower(), school or ELEMENT_SCHOOL[element], desc, learn=learn,
      ranks=ranks or None, fromTalent=from_talent, cooldown=cooldown,
      cost=mana(cost, pl(cost, cost_max, learn, last) if ranks else 0), target="Self",
      effects=[eff("SummonTotem", target="Self", summon=cid, totemElement=element, lifetime=lifetime)],
      tags=["Totem"], aiHint=ai[0], aiPriority=ai[1])
    c = {"id": cid, "name": name, "description": cdesc, "sprite": "totem_" + element.lower(), "type": "Totem",
         "rank": "Totem", "levelMin": 1, "levelMax": 60, "healthMult": health, "damageMult": 1, "armorMult": 0,
         "attackSpeed": 2, "moveSpeed": 0, "size": 1.2, "ai": "Totem", "totemElement": element}
    if abilities:
        c["abilities"] = [{"ability": a, "priority": 10} for a in abilities]
    if passives:
        c["passives"] = list(passives)
    c["immune"] = CC_IMMUNE
    c["xpMult"] = 0
    CREATURES.append(c)
    return cid


def pulse(aid, name, icon, school, desc, learn, ranks, effects, area_radius, affects, ai=("Buff", 10), tags=None):
    A(aid, name, icon, school, desc, learn=learn, ranks=ranks or None, hidden=True, time="OffGcd", target="Self",
      area={"shape": "Circle", "radius": area_radius, "centeredOnCaster": True, "affects": affects},
      effects=effects, tags=tags or ["Totem"], aiHint=ai[0], aiPriority=ai[1])


def buff_totem(aid, name, element, learn, ranks, cost, cost_max, lifetime, desc, cdesc, buff_id, buff_name, buff_icon,
               buff_desc, radius=20, affects="Allies", ai=("Buff", 3), cooldown=None, school=None, **aura):
    """Totem whose hidden pulse (used at the end of each owner turn) applies a short buff/debuff to units in range."""
    pid = aid + "_pulse"
    totem(aid, name, element, learn, ranks, cost, cost_max, lifetime, desc, cdesc, abilities=[pid], ai=ai,
          cooldown=cooldown, school=school)
    sch = school or ELEMENT_SCHOOL[element]
    pulse(pid, name, buff_icon, sch, buff_desc + " (within %d yards; refreshed at the end of the shaman's turn)." % radius,
          learn, ranks, [eff("ApplyAura", aura=buff_id)], radius, affects,
          ai=("Debuff" if affects == "Enemies" else "Buff", 10))
    U(buff_id, buff_name, buff_icon, buff_desc + ".", kind="Debuff" if affects == "Enemies" else "Buff", school=sch,
      duration=PULSE_BUFF_DURATION, tags=["Totem"], **aura)


# ---------------------------------------------------------------------------------------------------------- Earth
STONESKIN_R = [4, 14, 24, 34, 44, 54]
buff_totem("shaman_stoneskin_totem", "Stoneskin Totem", "Earth", 4, STONESKIN_R, 30, 105, 120,
           "Summons a Stoneskin Totem at the feet of the caster. The totem protects party members within 20 yards, "
           "reducing melee damage taken by 3 (rank 1) to 20 (rank 6) per hit. Lasts 2 min.",
           "An earth totem that hardens the skin of nearby allies.", "shaman_stoneskin", "Stoneskin", "rock",
           "Melee damage taken reduced by a flat amount per hit",
           absorb={"amount": 3, "perLevel": pl(3, 20, 4, 54), "schools": ["Physical"]}, special="ShamanStoneskin")

buff_totem("shaman_earthbind_totem", "Earthbind Totem", "Earth", 6, None, 50, 50, 45,
           "Summons an Earthbind Totem at the feet of the caster for 45 sec that slows the movement speed of enemies "
           "within 10 yards by 50%.",
           "An earth totem that grips the feet of nearby enemies.", "shaman_earthbind", "Earthbind", "totem_earth",
           "Movement speed reduced by 50%", radius=10, affects="Enemies", ai=("CC", 4), cooldown=15,
           mods=[mod("MoveSpeed", -50)])

buff_totem("shaman_strength_of_earth_totem", "Strength of Earth Totem", "Earth", 10, [10, 24, 38, 52, 60], 25, 275,
           120,
           "Summons a Strength of Earth Totem at the feet of the caster. The totem increases the Strength of party "
           "members within 20 yards by 10 (rank 1) to 77 (rank 5). Lasts 2 min.",
           "An earth totem that lends strength to nearby allies.", "shaman_strength_of_earth", "Strength of Earth",
           "totem_earth", "Strength increased", mods=[mod("Strength", 10, perLevel=pl(10, 77, 10, 60))])

STONECLAW_R = [8, 18, 28, 38, 48, 58]
totem("shaman_stoneclaw_totem", "Stoneclaw Totem", "Earth", 8, STONECLAW_R, 30, 145, 15,
      "Summons a sturdy Stoneclaw Totem at the feet of the caster for 15 sec that taunts creatures within 8 yards to "
      "attack it.",
      "A sturdy earth totem that draws enemies' attacks.", abilities=["shaman_stoneclaw_totem_taunt"], cooldown=30,
      health=2.2, ai=("Defensive", 4))
pulse("shaman_stoneclaw_totem_taunt", "Stoneclaw Taunt", "totem_earth", "Nature",
      "Taunts enemies within 8 yards to attack the totem.", 8, STONECLAW_R, [eff("Taunt")], 8, "Enemies",
      ai=("Taunt", 10))

totem("shaman_tremor_totem", "Tremor Totem", "Earth", 18, None, 60, 60, 120,
      "Summons a Tremor Totem at the feet of the caster that shakes the ground around it, removing Fear, Charm and "
      "Sleep effects from party members within 30 yards. Lasts 2 min.",
      "An earth totem whose tremors shake allies free of fear, charm and sleep.",
      abilities=["shaman_tremor_totem_pulse"], ai=("Defensive", 3))
pulse("shaman_tremor_totem_pulse", "Tremor", "totem_earth", "Nature",
      "Removes Fear, Charm and Sleep effects from party members within 30 yards.", 18, None,
      [eff("RemoveAura", auraTag="Fear"), eff("RemoveAura", auraTag="Charm"), eff("RemoveAura", auraTag="Sleep")],
      30, "Allies", ai=("Utility", 10))

# ----------------------------------------------------------------------------------------------------------- Fire
SEARING_R = [10, 20, 30, 40, 50, 60]
totem("shaman_searing_totem", "Searing Totem", "Fire", 10, SEARING_R, 25, 170, 55,
      "Summons a Searing Totem at the feet of the caster for 55 sec that repeatedly attacks an enemy within 20 yards "
      "for 9-11 (rank 1) to 40-54 (rank 6) Fire damage (about 2.4 attacks per round, resolved as one bolt at the end "
      "of your turn).",
      "A fire totem that hurls bolts of flame at nearby enemies.", abilities=["shaman_searing_totem_attack"],
      ai=("Damage", 5))
A("shaman_searing_totem_attack", "Searing Bolt", "totem_fire", "Fire",
  "The Searing Totem attacks an enemy within 20 yards for {0} Fire damage this round.",
  learn=10, ranks=SEARING_R, hidden=True, time="OffGcd", target="Enemy", range=20,
  effects=[eff("Damage", min=22, max=26, perLevel=pl(24, 113, 10, 60), coef=0.4)], tags=["Totem"],
  aiHint="Damage", aiPriority=10)

FIRE_NOVA_R = [12, 22, 32, 42, 52]
# lifetime 10: totems age 6 s before acting at the end of the owner turn; the blast kills the totem (WoW: 5 s)
totem("shaman_fire_nova_totem", "Fire Nova Totem", "Fire", 12, FIRE_NOVA_R, 95, 520, 10,
      "Summons a Fire Nova Totem at the feet of the caster. At the end of your turn (unless destroyed first) the "
      "totem explodes, inflicting 53-62 (rank 1) to 413-459 (rank 5) Fire damage to enemies within 10 yards, and is "
      "destroyed.",
      "A fire totem that erupts in a nova of flame.", abilities=["shaman_fire_nova_totem_blast"], cooldown=15,
      ai=("AoE", 5))
pulse("shaman_fire_nova_totem_blast", "Fire Nova", "totem_fire", "Fire",
      "The totem explodes for {0} Fire damage to enemies within 10 yards.", 12, FIRE_NOVA_R,
      [eff("Damage", min=53, max=62, perLevel=pl(57.5, 436, 12, 52), coef=0.214), eff("Kill", target="Self")],
      10, "Enemies", ai=("AoE", 10))

MAGMA_R = [26, 36, 46, 56]
totem("shaman_magma_totem", "Magma Totem", "Fire", 26, MAGMA_R, 230, 650, 20,
      "Summons a Magma Totem at the feet of the caster for 20 sec that causes 22 (rank 1) to 75 (rank 4) Fire damage "
      "to enemies within 8 yards every 2 sec (3 pulses per round, resolved at the end of your turn).",
      "A fire totem that pulses molten heat.", abilities=["shaman_magma_totem_pulse"], ai=("AoE", 4))
pulse("shaman_magma_totem_pulse", "Magma Pulse", "totem_fire", "Fire",
      "Deals {0} Fire damage to enemies within 8 yards (three pulses).", 26, MAGMA_R,
      [eff("Damage", min=66, perLevel=pl(66, 225, 26, 56), coef=0.1)], 8, "Enemies", ai=("AoE", 10))

buff_totem("shaman_flametongue_totem", "Flametongue Totem", "Fire", 28, [28, 38, 48, 58], 115, 275, 120,
           "Summons a Flametongue Totem at the feet of the caster. Party members within 20 yards gain the Flametongue "
           "effect: each melee hit causes 7-11 (rank 1) to about 28-32 (rank 4) additional Fire damage. Lasts 2 min.",
           "A fire totem that wreathes allied weapons in flame.", "shaman_flametongue_totem_buff", "Flametongue Totem",
           "totem_fire", "Melee hits deal additional Fire damage",
           procs=[{"trigger": "OnMeleeHit", "chance": 100,
                   "effects": [eff("Damage", target="Target", school="Fire", min=7, max=11,
                                   perLevel=pl(9, 30, 28, 58))]}])

buff_totem("shaman_frost_resistance_totem", "Frost Resistance Totem", "Fire", 24, [24, 38, 54], 135, 270, 120,
           "Summons a Frost Resistance Totem at the feet of the caster. The totem increases party members' Frost "
           "resistance by 30 (rank 1) to 60 (rank 3) within 20 yards. Lasts 2 min.",
           "A fire totem that wards allies against frost.", "shaman_frost_resistance", "Frost Resistance",
           "totem_fire", "Frost resistance increased", ai=("Buff", 1),
           mods=[mod("Resistance", 30, perLevel=pl(30, 60, 24, 54), school="Frost")])

# ---------------------------------------------------------------------------------------------------------- Water
HS_R = [20, 30, 40, 50, 60]
totem("shaman_healing_stream_totem", "Healing Stream Totem", "Water", 20, HS_R, 40, 80, 60,
      "Summons a Healing Stream Totem at the feet of the caster for 1 min that heals party members within 20 yards "
      "for 6 (rank 1) to 14 (rank 5) every 2 sec (3 pulses per round, resolved at the end of your turn).",
      "A water totem that sends forth gentle healing waters.", abilities=["shaman_healing_stream_totem_pulse"],
      ai=("Heal", 4))
pulse("shaman_healing_stream_totem_pulse", "Healing Stream", "totem_water", "Nature",
      "Heals party members within 20 yards for {0} (three pulses).", 20, HS_R,
      [eff("Heal", min=18, perLevel=pl(18, 42, 20, 60), coef=0.066)], 20, "Allies", ai=("Heal", 10),
      tags=["Totem", "Heal"])

MS_R = [26, 36, 46, 56]
totem("shaman_mana_spring_totem", "Mana Spring Totem", "Water", 26, MS_R, 40, 100, 60,
      "Summons a Mana Spring Totem at the feet of the caster for 1 min that restores 4 (rank 1) to 10 (rank 4) mana "
      "every 2 sec to party members within 20 yards (3 pulses per round, resolved at the end of your turn).",
      "A water totem that restores mana to nearby allies.", abilities=["shaman_mana_spring_totem_pulse"],
      ai=("Buff", 4))
pulse("shaman_mana_spring_totem_pulse", "Mana Spring", "totem_water", "Nature",
      "Restores {0} mana to party members within 20 yards (three pulses).", 26, MS_R,
      [eff("GainResource", resource="Mana", amount=12, perLevel=pl(12, 30, 26, 56))], 20, "Allies")

totem("shaman_poison_cleansing_totem", "Poison Cleansing Totem", "Water", 22, None, 50, 50, 120,
      "Summons a Poison Cleansing Totem at the feet of the caster that attempts to remove 1 poison effect from party "
      "members within 20 yards every 5 sec (once per round). Lasts 2 min.",
      "A water totem that purges poisons.", abilities=["shaman_poison_cleansing_totem_pulse"], ai=("Utility", 2))
pulse("shaman_poison_cleansing_totem_pulse", "Poison Cleansing", "totem_water", "Nature",
      "Removes 1 poison effect from each party member within 20 yards.", 22, None,
      [eff("Dispel", dispelType="Poison", dispelCount=1)], 20, "Allies", ai=("Utility", 10))

totem("shaman_disease_cleansing_totem", "Disease Cleansing Totem", "Water", 38, None, 50, 50, 120,
      "Summons a Disease Cleansing Totem at the feet of the caster that attempts to remove 1 disease effect from "
      "party members within 20 yards every 5 sec (once per round). Lasts 2 min.",
      "A water totem that cleanses disease.", abilities=["shaman_disease_cleansing_totem_pulse"], ai=("Utility", 2))
pulse("shaman_disease_cleansing_totem_pulse", "Disease Cleansing", "totem_water", "Nature",
      "Removes 1 disease effect from each party member within 20 yards.", 38, None,
      [eff("Dispel", dispelType="Disease", dispelCount=1)], 20, "Allies", ai=("Utility", 10))

buff_totem("shaman_fire_resistance_totem", "Fire Resistance Totem", "Water", 28, [28, 42, 58], 145, 285, 120,
           "Summons a Fire Resistance Totem at the feet of the caster. The totem increases party members' Fire "
           "resistance by 30 (rank 1) to 60 (rank 3) within 20 yards. Lasts 2 min.",
           "A water totem that wards allies against fire.", "shaman_fire_resistance", "Fire Resistance",
           "totem_water", "Fire resistance increased", ai=("Buff", 1),
           mods=[mod("Resistance", 30, perLevel=pl(30, 60, 28, 58), school="Fire")])

MT_R = [40, 48, 58]
# lifetime 13 (WoW 12 s): totems age 6 s before acting, so 13 s gives exactly two pulses (= 4 WoW ticks)
totem("shaman_mana_tide_totem", "Mana Tide Totem", "Water", 40, MT_R, 40, 60, 13,
      "Summons a Mana Tide Totem at the feet of the caster for 12 sec that restores 170 (rank 1) to 290 (rank 3) mana "
      "every 3 sec to party members within 20 yards (2 pulses per round, resolved at the end of your turn).",
      "A water totem that surges with restoring tides.", abilities=["shaman_mana_tide_totem_pulse"], cooldown=300,
      ai=("Buff", 6), from_talent=True)
pulse("shaman_mana_tide_totem_pulse", "Mana Tide", "totem_water", "Nature",
      "Restores {0} mana to party members within 20 yards (two pulses).", 40, MT_R,
      [eff("GainResource", resource="Mana", amount=340, perLevel=pl(340, 580, 40, 58))], 20, "Allies")

# ------------------------------------------------------------------------------------------------------------ Air
buff_totem("shaman_windfury_totem", "Windfury Totem", "Air", 32, [32, 42, 52], 95, 175, 120,
           "Summons a Windfury Totem at the feet of the caster. Party members within 20 yards gain Windfury on their "
           "melee weapons: each hit has a 20% chance of granting 1 extra attack with 122 (rank 1) to 315 (rank 3) "
           "extra attack power. Lasts 2 min.",
           "An air totem that quickens allied weapons.", "shaman_windfury_totem_buff", "Windfury Totem", "totem_air",
           "Melee hits have a 20% chance to grant an extra attack", ai=("Buff", 4),
           procs=[{"trigger": "OnMeleeHit", "chance": 20,
                   "effects": [eff("Special", target="Self", special="ShamanWindfuryAttack", count=1, min=122,
                                   perLevel=pl(122, 315, 32, 52))]}])

buff_totem("shaman_grace_of_air_totem", "Grace of Air Totem", "Air", 42, [42, 56, 60], 155, 310, 120,
           "Summons a Grace of Air Totem at the feet of the caster. Party members within 20 yards gain 43 (rank 1) to "
           "77 (rank 3) Agility. Lasts 2 min.",
           "An air totem that lends grace to nearby allies.", "shaman_grace_of_air", "Grace of Air", "totem_air",
           "Agility increased", mods=[mod("Agility", 43, perLevel=pl(43, 77, 42, 60))])

totem("shaman_grounding_totem", "Grounding Totem", "Air", 30, None, 90, 90, 45,
      "Summons a Grounding Totem at the feet of the caster that will redirect one harmful single-target spell cast on "
      "a nearby party member to itself every 10 seconds. Will not redirect area of effect spells. Lasts 45 sec.",
      "An air totem that draws hostile spells into itself.", passives=["shaman_grounding_totem_effect"],
      cooldown=15, ai=("Defensive", 3))
U("shaman_grounding_totem_effect", "Grounding Totem Effect", "totem_air",
  "Redirects one harmful spell cast on a party member within 20 yards to the totem every 10 sec.", school="Nature",
  hidden=True, special="ShamanGroundingTotem")

buff_totem("shaman_nature_resistance_totem", "Nature Resistance Totem", "Air", 30, [30, 44, 60], 160, 300, 120,
           "Summons a Nature Resistance Totem at the feet of the caster. The totem increases party members' Nature "
           "resistance by 30 (rank 1) to 60 (rank 3) within 20 yards. Lasts 2 min.",
           "An air totem that wards allies against nature.", "shaman_nature_resistance", "Nature Resistance",
           "totem_air", "Nature resistance increased", ai=("Buff", 1),
           mods=[mod("Resistance", 30, perLevel=pl(30, 60, 30, 60), school="Nature")])

buff_totem("shaman_windwall_totem", "Windwall Totem", "Air", 36, [36, 46, 56], 115, 225, 120,
           "Summons a Windwall Totem at the feet of the caster that reduces the damage of each ranged attack against "
           "party members within 20 yards by 10 (rank 1) to 25 (rank 3). Lasts 2 min.",
           "An air totem that turns aside arrows and bullets.", "shaman_windwall", "Windwall", "totem_air",
           "Ranged damage taken reduced by a flat amount per hit", ai=("Buff", 2),
           absorb={"amount": 10, "perLevel": pl(10, 25, 36, 56), "schools": ["Physical"]}, special="ShamanWindwall")

buff_totem("shaman_tranquil_air_totem", "Tranquil Air Totem", "Air", 50, None, 50, 50, 120,
           "Summons a Tranquil Air Totem at the feet of the caster. Party members within 20 yards generate 20% less "
           "threat. Lasts 2 min.",
           "An air totem that calms the minds of enemies towards your allies.", "shaman_tranquil_air", "Tranquil Air",
           "totem_air", "Threat generated reduced by 20%", ai=("Buff", 2), mods=[mod("ThreatGenerated", -20)])

# =====================================================================================================================
# Talent abilities
# =====================================================================================================================
A("shaman_elemental_mastery", "Elemental Mastery", "sparkle", "Nature",
  "When activated, your next Fire, Frost, or Nature damage spell has a 100% critical strike chance and costs no mana.",
  learn=40, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
  effects=[eff("ApplyAura", aura="shaman_elemental_mastery")], aiHint="Buff", aiPriority=6)
U("shaman_elemental_mastery", "Elemental Mastery", "sparkle",
  "Your next Fire, Frost, or Nature damage spell is a critical strike and costs no mana.", school="Nature",
  duration=0, charges=1,
  mods=[mod("SpellCrit", 100, school=s) for s in ("Fire", "Frost", "Nature")] +
       [mod("ManaCost", -100, school=s) for s in ("Fire", "Frost", "Nature")],
  procs=[{"trigger": "OnSpellCast", "chance": 100, "abilities": DMG_SPELLS, "consumeCharge": True, "effects": []}])

A("shaman_stormstrike", "Stormstrike", "fist", "Nature",
  "Gives you an extra attack ({0}). In addition, the next 2 sources of Nature damage dealt to the target are "
  "increased by 20%. Lasts 12 sec.",
  learn=40, fromTalent=True, cooldown=20, cost={"type": "Mana", "pctBaseMana": 8}, target="Enemy", melee=True,
  requires={"meleeWeapon": True},
  effects=[eff("WeaponDamage", weaponPct=100), eff("ApplyAura", aura="shaman_stormstrike")],
  aiHint="Damage", aiPriority=8)
U("shaman_stormstrike", "Stormstrike", "fist", "Nature damage taken increased by 20% (2 charges).", kind="Debuff",
  school="Nature", dispel="Magic", duration=12, charges=2, mods=[mod("DamageTaken", 20, school="Nature")],
  procs=[{"trigger": "OnDamaged", "chance": 100, "schools": ["Nature"], "consumeCharge": True, "effects": []}])

A("shaman_natures_swiftness", "Nature's Swiftness", "leaf", "Nature",
  "When activated, your next Nature spell with a casting time less than 10 sec. becomes an instant cast spell.",
  learn=30, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
  effects=[eff("ApplyAura", aura="shaman_natures_swiftness")], aiHint="Buff", aiPriority=3)
U("shaman_natures_swiftness", "Nature's Swiftness", "leaf",
  "Your next Nature spell with a casting time less than 10 sec is instant.", school="Nature", duration=0, charges=1,
  special="ShamanNaturesSwiftness")

A("shaman_parry", "Parry", "swords", "Physical", "Gives a chance to parry enemy melee attacks.", learn=30,
  fromTalent=True, passive=True, passiveAura="shaman_parry")
U("shaman_parry", "Parry", "swords", "Able to parry melee attacks.", hidden=True, mods=[mod("Parry", 5)])

# Talent proc auras ----------------------------------------------------------------------------------------------------
U("shaman_clearcasting", "Clearcasting", "sparkle", "Your next damage spell costs no mana.", school="Nature",
  duration=15, charges=1, mods=[mod("ManaCost", -100, school=s) for s in ("Fire", "Frost", "Nature")],
  procs=[{"trigger": "OnSpellCast", "chance": 100, "abilities": DMG_SPELLS, "consumeCharge": True, "effects": []}])
U("shaman_focused_casting", "Focused Casting", "storm", "Taking damage does not delay your spellcasting.",
  school="Nature", duration=6, special="ShamanPushbackResist")
U("shaman_elemental_devastation", "Elemental Devastation", "storm", "Melee critical strike chance increased.",
  school="Nature", duration=10, mods=[mod("MeleeCrit", values=[3, 6, 9])])
U("shaman_flurry", "Flurry", "wind", "Attack speed increased for the next 3 swings.", duration=15, charges=3,
  mods=[mod("MeleeHaste", values=[10, 15, 20, 25, 30])],
  procs=[{"trigger": "OnAutoAttackHit", "chance": 100, "consumeCharge": True, "effects": []}])
U("shaman_ancestral_fortitude", "Ancestral Fortitude", "armor", "Armor increased.", school="Nature", duration=15,
  mods=[mod("Armor", values=[8, 16, 25], pct=True)])
U("shaman_healing_way", "Healing Way", "wave", "Healing received increased by 6% per stack.", school="Nature",
  duration=15, maxStacks=3, mods=[mod("HealingTaken", 6)])

# =====================================================================================================================
# Talents
# =====================================================================================================================
EL, EN, RE = "shaman_ele_", "shaman_enh_", "shaman_resto_"
FIRE_TOTEM_FX = ["shaman_searing_totem_attack", "shaman_magma_totem_pulse", "shaman_fire_nova_totem_blast"]
PULSE_FRIENDLY = [t + "_pulse" for t in (
    "shaman_stoneskin_totem", "shaman_strength_of_earth_totem", "shaman_flametongue_totem",
    "shaman_frost_resistance_totem", "shaman_healing_stream_totem", "shaman_mana_spring_totem",
    "shaman_poison_cleansing_totem", "shaman_disease_cleansing_totem", "shaman_fire_resistance_totem",
    "shaman_mana_tide_totem", "shaman_windfury_totem", "shaman_grace_of_air_totem", "shaman_nature_resistance_totem",
    "shaman_windwall_totem", "shaman_tranquil_air_totem")]

ele = [
    talent(EL + "convection", "Convection", "lightning", 1, 1, 5,
           "Reduces the mana cost of your Shock, Lightning Bolt and Chain Lightning spells by {2/4/6/8/10}%.",
           [amod("Cost", -2, tags=["Shock"], abilities=LIGHTNING)]),
    talent(EL + "concussion", "Concussion", "storm", 1, 2, 5,
           "Increases the damage done by your Lightning Bolt, Chain Lightning and Shock spells by {1/2/3/4/5}%.",
           [amod("Damage", 1, tags=["Shock"], abilities=LIGHTNING)]),
    talent(EL + "earths_grasp", "Earth's Grasp", "earth", 2, 0, 2,
           "Increases the health of your Stoneclaw Totem by {25/50}% and the radius of your Earthbind Totem by "
           "{10/20}%.",
           [amod("Radius", 10, abilities=["shaman_earthbind_totem_pulse"], target="Pet"),
            special_p("ShamanEarthsGrasp", values=[25, 50])]),
    talent(EL + "elemental_warding", "Elemental Warding", "shield", 2, 1, 3,
           "Reduces damage taken from Fire, Frost and Nature effects by {4/7/10}%.",
           [stat_p("DamageTaken", values=[-4, -7, -10], school=s) for s in ("Fire", "Frost", "Nature")]),
    talent(EL + "call_of_flame", "Call of Flame", "totem_fire", 2, 2, 3,
           "Increases the damage done by your Fire Totems by {5/10/15}%.",
           [amod("Damage", 5, abilities=FIRE_TOTEM_FX, target="Pet")]),
    talent(EL + "elemental_focus", "Elemental Focus", "sparkle", 3, 0, 1,
           "Gives you a 10% chance to enter a Clearcasting state after casting any Fire, Frost, or Nature damage "
           "spell. The Clearcasting state reduces the mana cost of your next damage spell by 100%.",
           [proc_p({"trigger": "OnSpellCast", "chance": 10, "abilities": DMG_SPELLS,
                    "effects": [eff("ApplyAura", target="Self", aura="shaman_clearcasting")]})]),
    talent(EL + "reverberation", "Reverberation", "earth", 3, 1, 5,
           "Reduces the cooldown of your Shock spells by {0.2/0.4/0.6/0.8/1} sec.",
           [amod("Cooldown", -0.2, tags=["Shock"])]),
    talent(EL + "call_of_thunder", "Call of Thunder", "lightning", 3, 2, 5,
           "Increases the critical strike chance of your Lightning Bolt and Chain Lightning spells by an additional "
           "{1/2/3/4/6}%.", [amod("CritChance", values=[1, 2, 3, 4, 6], abilities=LIGHTNING)]),
    talent(EL + "improved_fire_totems", "Improved Fire Totems", "totem_fire", 4, 0, 2,
           "Reduces the delay before your Fire Nova Totem activates by {1/2} sec. (it already explodes at the end of "
           "your turn in Lanternvale) and decreases the threat generated by your Magma Totem by {25/50}%.",
           [amod("Threat", -25, abilities=["shaman_magma_totem_pulse"], target="Pet")]),
    talent(EL + "eye_of_the_storm", "Eye of the Storm", "storm", 4, 1, 3,
           "Gives you a {33/66/100}% chance to gain the Focused Casting effect that lasts for 6 sec after being the "
           "victim of a melee or ranged critical strike. The Focused Casting effect prevents you from losing casting "
           "time when taking damage.",
           [proc_p({"trigger": "OnCritTaken",
                    "effects": [eff("ApplyAura", target="Self", aura="shaman_focused_casting")]},
                   values=[33, 66, 100])]),
    talent(EL + "elemental_devastation", "Elemental Devastation", "storm", 4, 3, 3,
           "Your offensive spell crits will increase your chance to get a critical strike with melee attacks by "
           "{3/6/9}% for 10 sec.",
           [proc_p({"trigger": "OnSpellCrit", "chance": 100, "abilities": DMG_SPELLS,
                    "effects": [eff("ApplyAura", target="Self", aura="shaman_elemental_devastation")]})]),
    talent(EL + "storm_reach", "Storm Reach", "chain_lightning", 5, 0, 2,
           "Increases the range of your Lightning Bolt and Chain Lightning spells by {3/6} yards.",
           [amod("Range", 10, abilities=LIGHTNING)]),
    talent(EL + "elemental_fury", "Elemental Fury", "fire", 5, 1, 1,
           "Increases the critical strike damage bonus of your Searing, Magma, and Fire Nova Totems and your Fire, "
           "Frost, and Nature spells by 100%.",
           [amod("CritBonus", 100, abilities=DMG_SPELLS), amod("CritBonus", 100, abilities=FIRE_TOTEM_FX, target="Pet")]),
    talent(EL + "lightning_mastery", "Lightning Mastery", "lightning", 6, 2, 5,
           "Reduces the cast time of your Lightning Bolt and Chain Lightning spells by {0.2/0.4/0.6/0.8/1} sec.",
           [amod("CastTime", -0.2, abilities=LIGHTNING)]),
    talent(EL + "elemental_mastery", "Elemental Mastery", "sparkle", 7, 1, 1,
           "When activated, this spell gives your next Fire, Frost, or Nature damage spell a 100% critical strike "
           "chance and reduces the mana cost by 100%. 3 min cooldown.", [grant("shaman_elemental_mastery")],
           requires=EL + "elemental_fury"),
]

enh = [
    talent(EN + "ancestral_knowledge", "Ancestral Knowledge", "brain", 1, 1, 5,
           "Increases your maximum Mana by {1/2/3/4/5}%.", [stat_p("Mana", 1, pct=True)]),
    talent(EN + "shield_specialization", "Shield Specialization", "shield", 1, 2, 5,
           "Increases your chance to block attacks with a shield by {1/2/3/4/5}% and increases the amount blocked by "
           "{5/10/15/20/25}%.", [stat_p("Block", 1), stat_p("BlockValue", 5, pct=True)]),
    talent(EN + "guardian_totems", "Guardian Totems", "totem_earth", 2, 0, 2,
           "Increases the amount of damage reduced by your Stoneskin Totem and Windwall Totem by {10/20}% and reduces "
           "the cooldown of your Grounding Totem by {1/2} sec.",
           [amod("Effect", 10, abilities=["shaman_stoneskin_totem_pulse", "shaman_windwall_totem_pulse"], target="Pet"),
            amod("Cooldown", -1, abilities=["shaman_grounding_totem"])]),
    talent(EN + "thundering_strikes", "Thundering Strikes", "lightning", 2, 1, 5,
           "Improves your chance to get a critical strike with your weapon attacks by {1/2/3/4/5}%.",
           [stat_p("MeleeCrit", 1)]),
    talent(EN + "improved_ghost_wolf", "Improved Ghost Wolf", "wolf_spirit", 2, 2, 2,
           "Reduces the cast time of your Ghost Wolf spell by {1/2} sec.",
           [amod("CastTime", -1, abilities=["shaman_ghost_wolf"])]),
    talent(EN + "improved_lightning_shield", "Improved Lightning Shield", "storm", 2, 3, 3,
           "Increases the damage done by your Lightning Shield orbs by {5/10/15}%.",
           [amod("Damage", 5, abilities=["shaman_lightning_shield"])]),
    talent(EN + "enhancing_totems", "Enhancing Totems", "totem_earth", 3, 0, 2,
           "Increases the effect of your Strength of Earth and Grace of Air Totems by {8/15}%.",
           [amod("Effect", values=[8, 15], abilities=["shaman_strength_of_earth_totem_pulse",
                                                      "shaman_grace_of_air_totem_pulse"], target="Pet")]),
    talent(EN + "two_handed_axes_and_maces", "Two-Handed Axes and Maces", "axe", 3, 2, 1,
           "Allows you to use Two-Handed Axes and Two-Handed Maces.", [special_p("ShamanTwoHandedWeapons")]),
    talent(EN + "anticipation", "Anticipation", "eye", 3, 3, 5,
           "Increases your chance to dodge by an additional {1/2/3/4/5}%.", [stat_p("Dodge", 1)]),
    talent(EN + "flurry", "Flurry", "wind", 4, 1, 5,
           "Increases your attack speed by {10/15/20/25/30}% for your next 3 swings after dealing a critical strike.",
           [proc_p({"trigger": "OnMeleeCrit", "chance": 100,
                    "effects": [eff("ApplyAura", target="Self", aura="shaman_flurry")]})],
           requires=EN + "thundering_strikes"),
    talent(EN + "toughness", "Toughness", "armor", 4, 2, 5,
           "Increases your armor value from items by {2/4/6/8/10}%.", [stat_p("Armor", 2, pct=True)]),
    talent(EN + "improved_weapon_totems", "Improved Weapon Totems", "totem_air", 5, 0, 2,
           "Increases the melee attack power bonus of your Windfury Totem by {15/30}% and increases the damage caused "
           "by your Flametongue Totem by {6/12}%.",
           [amod("Effect", values=[15, 30], abilities=["shaman_windfury_totem_pulse"], target="Pet"),
            amod("Damage", values=[6, 12], abilities=["shaman_flametongue_totem_pulse"], target="Pet")]),
    talent(EN + "elemental_weapons", "Elemental Weapons", "ember", 5, 1, 3,
           "Increases the melee attack power bonus of your Rockbiter Weapon by {7/14/20}%, your Frostbrand Weapon "
           "damage by {7/14/20}% and your Flametongue Weapon damage by {5/10/15}%.",
           [amod("Effect", values=[7, 14, 20], abilities=["shaman_rockbiter_weapon"]),
            amod("Damage", values=[7, 14, 20], abilities=["shaman_frostbrand_weapon"]),
            amod("Damage", values=[5, 10, 15], abilities=["shaman_flametongue_weapon"])]),
    talent(EN + "parry", "Parry", "swords", 5, 2, 1,
           "Gives a chance to parry enemy melee attacks.", [grant("shaman_parry")]),
    talent(EN + "weapon_mastery", "Weapon Mastery", "mace", 6, 2, 5,
           "Increases the damage you deal with all weapons by {2/4/6/8/10}%.",
           [stat_p("DamageDone", 2, school="Physical")]),
    talent(EN + "stormstrike", "Stormstrike", "fist", 7, 1, 1,
           "Gives you an extra attack. In addition, the next 2 sources of Nature damage dealt to the target are "
           "increased by 20%. Lasts 12 sec.", [grant("shaman_stormstrike")], requires=EN + "elemental_weapons"),
]

resto = [
    talent(RE + "improved_healing_wave", "Improved Healing Wave", "wave", 1, 1, 5,
           "Reduces the casting time of your Healing Wave spell by {0.1/0.2/0.3/0.4/0.5} sec.",
           [amod("CastTime", -0.1, abilities=["shaman_healing_wave"])]),
    talent(RE + "tidal_focus", "Tidal Focus", "water_drop", 1, 2, 5,
           "Reduces the mana cost of your healing spells by {1/2/3/4/5}%.", [amod("Cost", -1, tags=["Heal"])]),
    talent(RE + "improved_reincarnation", "Improved Reincarnation", "halo", 2, 0, 2,
           "Reduces the cooldown of your Reincarnation spell by {10/20} min and increases the amount of health and "
           "mana you reincarnate with by an additional {10/20}%.",
           [amod("Cooldown", -600, abilities=["shaman_reincarnation"]),
            amod("Effect", 50, abilities=["shaman_reincarnation"])]),
    talent(RE + "ancestral_healing", "Ancestral Healing", "heal_plus", 2, 1, 3,
           "Increases your target's armor value by {8/16/25}% for 15 sec after getting a critical effect from one of "
           "your healing spells.",
           [proc_p({"trigger": "OnSpellCrit", "chance": 100, "tags": ["Heal"],
                    "effects": [eff("ApplyAura", target="Target", aura="shaman_ancestral_fortitude")]})]),
    talent(RE + "totemic_focus", "Totemic Focus", "totem", 2, 2, 5,
           "Reduces the mana cost of your totems by {5/10/15/20/25}%.", [amod("Cost", -5, tags=["Totem"])]),
    talent(RE + "natures_guidance", "Nature's Guidance", "leaf", 3, 0, 3,
           "Increases your chance to hit with melee attacks and spells by {1/2/3}%.",
           [stat_p("MeleeHit", 1), stat_p("SpellHit", 1)]),
    talent(RE + "healing_focus", "Healing Focus", "hands_pray", 3, 1, 5,
           "Gives you a {14/28/42/56/70}% chance to avoid interruption caused by damage while casting any healing "
           "spell.", [special_p("ShamanPushbackResist", values=[14, 28, 42, 56, 70])]),
    talent(RE + "totemic_mastery", "Totemic Mastery", "totem", 3, 2, 1,
           "The radius of your totems that affect friendly targets is increased to 30 yards.",
           [amod("Radius", 50, abilities=[a for a in PULSE_FRIENDLY if a != "shaman_tremor_totem_pulse"],
                 target="Pet")]),
    talent(RE + "healing_grace", "Healing Grace", "hands_pray", 3, 3, 3,
           "Reduces the threat generated by your healing spells by {5/10/15}%.",
           [amod("Threat", -5, tags=["Heal"]),
            amod("Threat", -5, abilities=["shaman_healing_stream_totem_pulse"], target="Pet")]),
    talent(RE + "restorative_totems", "Restorative Totems", "totem_water", 4, 1, 5,
           "Increases the effect of your Mana Spring and Healing Stream Totems by {5/10/15/20/25}%.",
           [amod("Healing", 5, abilities=["shaman_healing_stream_totem_pulse"], target="Pet"),
            amod("Effect", 5, abilities=["shaman_mana_spring_totem_pulse"], target="Pet")]),
    talent(RE + "tidal_mastery", "Tidal Mastery", "wave", 4, 2, 5,
           "Increases the critical effect chance of your healing and lightning spells by {1/2/3/4/5}%.",
           [amod("CritChance", 1, tags=["Heal"], abilities=LIGHTNING)]),
    talent(RE + "healing_way", "Healing Way", "wave", 5, 0, 3,
           "Your Healing Wave spells have a {33/66/100}% chance to increase the effect of subsequent Healing Wave "
           "spells on that target by 6% for 15 sec. This effect will stack up to 3 times.",
           [proc_p({"trigger": "OnHealCast", "abilities": ["shaman_healing_wave"],
                    "effects": [eff("ApplyAura", target="Target", aura="shaman_healing_way")]},
                   values=[33, 66, 100])]),
    talent(RE + "natures_swiftness", "Nature's Swiftness", "leaf", 5, 2, 1,
           "When activated, your next Nature spell with a casting time less than 10 sec. becomes an instant cast "
           "spell.", [grant("shaman_natures_swiftness")]),
    talent(RE + "purification", "Purification", "heal_plus", 6, 2, 5,
           "Increases the effectiveness of your healing spells by {2/4/6/8/10}%.",
           [amod("Healing", 2, tags=["Heal"]),
            amod("Healing", 2, abilities=["shaman_healing_stream_totem_pulse"], target="Pet")]),
    talent(RE + "mana_tide_totem", "Mana Tide Totem", "totem_water", 7, 1, 1,
           "Summons a Mana Tide Totem with 5 health at the feet of the caster for 12 sec that restores 170 mana every "
           "3 seconds to group members within 20 yards.", [grant("shaman_mana_tide_totem")]),
]

TREES = [
    {"id": "tree_shaman_elemental", "classId": "Shaman", "name": "Elemental", "icon": "lightning",
     "description": "Command the storm: stronger, cheaper Lightning and Shocks, Elemental Fury and Elemental Mastery.",
     "talents": ele},
    {"id": "tree_shaman_enhancement", "classId": "Shaman", "name": "Enhancement", "icon": "fist",
     "description": "The spirit-touched warrior: weapon imbues, Flurry, two-handed weapons and Stormstrike.",
     "talents": enh},
    {"id": "tree_shaman_restoration", "classId": "Shaman", "name": "Restoration", "icon": "water_drop",
     "description": "Healer of the tribe: cheaper, stronger heals, Nature's Swiftness and Mana Tide Totem.",
     "talents": resto},
]

BUILD = ([EN + "ancestral_knowledge"] * 5 + [EN + "thundering_strikes"] * 5 + [EN + "two_handed_axes_and_maces"] +
         [EN + "improved_ghost_wolf"] * 2 + [EN + "enhancing_totems"] * 2 + [EN + "flurry"] * 5 +
         [EN + "elemental_weapons"] * 3 + [EN + "improved_weapon_totems"] * 2 + [EN + "weapon_mastery"] * 5 +
         [EN + "stormstrike"] + [EN + "anticipation"] * 5 + [EN + "toughness"] * 5 + [EN + "parry"] +
         [EN + "guardian_totems"] * 2 + [EN + "improved_lightning_shield"] * 3 + [EL + "concussion"] * 4)

# =====================================================================================================================
# Items
# =====================================================================================================================
ITEMS = [
    {"id": "shaman_starter_worn_mace", "name": "Worn Mace", "icon": "mace",
     "description": "A knobbly club of seasoned wood, banded with copper.", "kind": "Weapon", "quality": "Common",
     "itemLevel": 2, "requiredLevel": 1, "equip": "OneHand", "weaponType": "OneHandMace", "minDamage": 2,
     "maxDamage": 5, "speed": 2.2, "price": 9},
    {"id": "shaman_starter_battered_buckler", "name": "Battered Buckler", "icon": "shield",
     "description": "A small hide-covered shield painted with a faded spiral.", "kind": "Armor", "quality": "Common",
     "itemLevel": 2, "requiredLevel": 1, "equip": "OffHand", "weaponType": "Shield", "armor": 15, "block": 2,
     "price": 8},
    {"id": "shaman_starter_primitive_mantle", "name": "Primitive Mantle", "icon": "armor",
     "description": "A fur-trimmed leather mantle hung with bone charms.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "requiredLevel": 1, "equip": "Chest", "armorType": "Leather", "armor": 12, "price": 5},
    {"id": "shaman_starter_primitive_kilt", "name": "Primitive Kilt", "icon": "armor",
     "description": "A wrap of tough hide, comfortable for long walks between spirit stones.", "kind": "Armor",
     "quality": "Common", "itemLevel": 1, "requiredLevel": 1, "equip": "Legs", "armorType": "Leather", "armor": 10,
     "price": 5},
    {"id": "shaman_ankh", "name": "Ankh", "icon": "halo",
     "description": "A small carved ankh. Consumed by a Shaman's Reincarnation.", "kind": "Reagent",
     "quality": "Common", "itemLevel": 30, "stack": 10, "price": 2000, "classes": ["Shaman"]},
]

CLASS = {
    "id": "Shaman", "name": "Shaman",
    "description": "A spiritual guide who commands the elements: lightning and shocks, imbued weapons, healing "
                   "waves and totems of earth, fire, water and air.",
    "roles": ["Healer", "Melee DPS", "Ranged DPS", "Support"], "color": "#0070DE", "icon": "totem",
    "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth", "Leather"], "armorUpgrade": {"level": 40, "type": "Mail"},
    "weaponTypes": ["OneHandMace", "OneHandAxe", "Staff", "Dagger", "FistWeapon", "Shield"],
    "dualWieldLevel": 0, "canParry": False, "canBlock": True,
    "baseStatsLevel1": {"strength": 21, "agility": 20, "stamina": 21, "intellect": 21, "spirit": 23},
    "baseStatsLevel60": {"strength": 98, "agility": 51, "stamina": 105, "intellect": 90, "spirit": 98},
    "baseHealthLevel1": 37, "baseHealthLevel60": 1423, "baseManaLevel1": 55, "baseManaLevel60": 1520,
    "meleeAp": "StrengthTimesTwo",
    "agilityPerMeleeCritAt60": 20, "intellectPerSpellCritAt60": 59.5, "agilityPerDodgeAt60": 20,
    "baseMeleeCrit": 5, "baseSpellCrit": 2.2, "baseDodge": 5,
    "manaRegenBase": 17, "manaRegenPerSpirit": 0.2,
    "basicAttack": "attack",
    "startingAbilities": ["attack", "shaman_lightning_bolt", "shaman_healing_wave", "shaman_rockbiter_weapon"],
    "startingItems": ["shaman_starter_worn_mace", "shaman_starter_battered_buckler", "shaman_starter_primitive_mantle",
                      "shaman_starter_primitive_kilt"],
    "startingStance": "",
    "talentTrees": [t["id"] for t in TREES],
    "defaultBuild": BUILD,
    "sprite": "char_shaman", "portrait": "portrait_shaman",
    "designNotes": "The Shaman is a hybrid who channels the elements: Lightning Bolt and Shocks at range, imbued "
                   "weapons and Stormstrike in melee, Healing Wave and Chain Heal for the party. Drop one totem of each "
                   "element - Earth, Fire, Water and Air - to empower allies or hinder foes; totems act at the end of "
                   "your turn. Shocks share a cooldown, so choose between Earth Shock's interrupt, Flame Shock's burn "
                   "and Frost Shock's snare.",
}

SPECIALS = [
    {"id": "ShamanWindfuryAttack",
     "usedBy": "shaman_windfury_weapon (aura proc, count 2), shaman_windfury_totem_buff (aura proc, count 1)",
     "behaviour": "Proc effect (target Self = the aura bearer). Immediately performs `count` extra main-hand melee "
                  "swings against the unit that was hit (if still alive and in reach), outside the normal swing timer. "
                  "Each extra swing uses the normal melee hit table and weapon damage with attack power temporarily "
                  "increased by min + perLevel x (effLevel - learnLevel) of the source ability's rank (46..333 for "
                  "the weapon, 122..315 for the totem), multiplied by the source aura's EffectMult (Improved Weapon "
                  "Totems +15/30% for the totem). Extra swings can crit and trigger other on-hit procs "
                  "(Flametongue, Flurry, Lightning Shield on the target...) but can never trigger "
                  "ShamanWindfuryAttack themselves. They do not reset or consume the swing timer. Only one Windfury "
                  "proc per triggering hit."},
    {"id": "ShamanStoneskin", "usedBy": "shaman_stoneskin (buff applied by shaman_stoneskin_totem_pulse)",
     "behaviour": "The aura's `absorb` is NOT a depleting shield: skip the generic absorb pool for this aura. Instead "
                  "every incoming melee hit (white swing or Physical melee ability) on the bearer is reduced by "
                  "absorb.amount + absorb.perLevel x (effLevel - learnLevel) (3 at level 4 ... 20 at level 54; the "
                  "pulse is cast by the totem so effLevel = totem level = owner level), multiplied by the aura's "
                  "EffectMult (Guardian Totems +10/20% via owner 'Pet' AbilityMods on the pulse), applied after "
                  "armor, never below 0. Ranged attacks and spells are unaffected. Several Stoneskin auras do not "
                  "stack (same aura id)."},
    {"id": "ShamanWindwall", "usedBy": "shaman_windwall (buff applied by shaman_windwall_totem_pulse)",
     "behaviour": "Same as ShamanStoneskin but for ranged attacks: every incoming ranged weapon attack (Auto Shot, "
                  "Shoot, thrown, ranged-table abilities) on the bearer is reduced by the absorb amount scaled by "
                  "level (10 at 36 ... 25 at 56) and EffectMult (Guardian Totems), never below 0. Skip the generic "
                  "absorb pool."},
    {"id": "ShamanEarthsGrasp", "usedBy": EL + "earths_grasp",
     "behaviour": "Stoneclaw Totems (creature shaman_totem_stoneclaw) summoned by a shaman with this talent have their "
                  "maximum (and current) health multiplied by 1 + values[rank]/100 (25/50%). Implement in "
                  "Specials.OnSummoned. (The Earthbind radius part is the talent's AbilityMod on the pulse.)"},
    {"id": "ShamanGroundingTotem", "usedBy": "shaman_grounding_totem_effect (passive of shaman_totem_grounding)",
     "behaviour": "While the totem lives, the first harmful single-target spell (ability with a non-Physical school, "
                  "target Enemy, no area) cast by a hostile unit at a member of the totem owner's party (including "
                  "pets, excluding totems) within 20 yards of the totem is redirected to the totem: the totem becomes "
                  "the spell's target and suffers its effects instead (damage normally destroys it; CC is ignored "
                  "because totems are immune). After a redirect the effect is inactive for 10 sec of game time "
                  "(rounds count 6 s). Area and chain spells, melee and ranged weapon attacks are never redirected."},
    {"id": "ShamanReincarnation", "usedBy": "shaman_reincarnation (passive aura)",
     "behaviour": "When the shaman becomes Downed (or dead) in combat while the ability shaman_reincarnation is off "
                  "cooldown and the party inventory holds a 'shaman_ankh', she is flagged 'can reincarnate'. When her "
                  "turn slot next comes up (Downed units normally skip it), offer 'Reincarnate' instead of skipping "
                  "(the player chooses; companion AI always accepts). Accepting consumes one Ankh, clears Downed/Dead "
                  "and revives her in place with 20% x (1 + Effect AbilityMod%) of maximum health and mana (Improved "
                  "Reincarnation +50/100% => 30/40%), starts the ability's cooldown (3600 s + Cooldown mods: "
                  "-600/-1200 s; recovers in real time out of combat), and she then takes that turn normally. "
                  "Declining leaves her Downed (allies can still Help her up)."},
    {"id": "ShamanNaturesSwiftness", "usedBy": "shaman_natures_swiftness (aura)",
     "behaviour": "The next ability the bearer uses whose school is Nature and whose (modified) castTime is > 0 and "
                  "< 10 s has castTime 0 (its Time cost becomes the GCD, it can never become a pending cast). The aura "
                  "is consumed by that cast. Channeled abilities are not affected."},
    {"id": "ShamanTwoHandedWeapons", "usedBy": EN + "two_handed_axes_and_maces",
     "behaviour": "While the talent is learned the shaman is proficient with TwoHandAxe and TwoHandMace (added to the "
                  "class weaponTypes for equip checks). Unlearning it (respec) unequips such weapons to the bags."},
    {"id": "ShamanPushbackResist", "usedBy": RE + "healing_focus (values 14..70 %), shaman_focused_casting (aura, 100 %)",
     "behaviour": "Only meaningful if the engine models WoW spell pushback (damage taken while a cast is pending "
                  "delays it). Healing Focus: pending casts of abilities tagged Heal ignore pushback with "
                  "values[rank]% chance per damage event. Focused Casting aura: all pending casts of the bearer "
                  "ignore pushback while it lasts. If pushback is not modelled, both are no-ops."},
]

bundle = {
    "_note": "Shaman (WoW Classic 1.12). Totems are SummonTotem units (one per element) that act at the end of the "
             "owner's turn by using their hidden abilities ('pulses'). Buff/debuff totems pulse a 10 s aura on units "
             "in range each owner turn (it lingers about one turn after leaving range, like WoW totem buffs), so they "
             "scale with the totem's level (= owner level) through perLevel and receive the owner's talents through "
             "AbilityMods with target 'Pet' (Enhancing Totems, Guardian Totems, Improved Weapon Totems, Totemic "
             "Mastery, Call of Flame, Restorative Totems...). Periodic totems are folded to one pulse per round "
             "(Healing Stream/Mana Spring/Magma x3, Searing x2.4, Mana Tide x2). Non-scalable per-rank values "
             "(cast times, durations, dispel counts) use the max-rank value, like the other class files.",
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
    print("shaman: abilities", len(B.abilities), "(trainable", len(trainable), ") auras", len(B.auras),
          "talents", [len(t["talents"]) for t in TREES], "items", len(ITEMS), "creatures", len(CREATURES),
          "specials", len(SPECIALS), "build", spent)
