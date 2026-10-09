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
totem("shaman_fire_nova_totem", "Fire Nova Totem", "Fire", 12, FIRE_NOVA_R, 95, 520, 5,
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
totem("shaman_mana_tide_totem", "Mana Tide Totem", "Water", 40, MT_R, 40, 60, 12,
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

