from mwcommon import *

CID = "Mage"


def A(id, name, icon, school, desc, learn, ranks=None, **kw):
    a = {"id": id, "name": name, "icon": icon, "classId": CID, "school": school, "description": desc,
         "learnLevel": learn}
    if ranks:
        assert ranks[0] == learn, id
        a["rankLevels"] = ranks
    if not kw.get("hidden"):
        a["trainCost"] = 0 if (kw.get("fromTalent") and not ranks) else train_cost(learn)
    a.update(kw)
    return a


def AU(id, name, icon, desc, kind="Buff", school="Arcane", **kw):
    a = {"id": id, "name": name, "icon": icon, "description": desc, "kind": kind, "school": school}
    a.update(kw)
    return a


# --------------------------------------------------------------------------- rank tables (WoW Classic 1.12)
FB_R = [1, 6, 12, 18, 24, 30, 36, 42, 48, 54, 60]
FROST_R = [4, 8, 14, 20, 26, 32, 38, 44, 50, 56, 60]          # R11 = Tome of Frostbolt XI (AQ) at 60
AM_R = [8, 16, 24, 32, 40, 48, 56, 60]                         # R8 = Tome of Arcane Missiles VIII (AQ) at 60
FBL_R = [6, 14, 22, 30, 38, 46, 54]
FN_R = [10, 26, 40, 54]
AE_R = [14, 22, 30, 38, 46, 54]
BZ_R = [20, 28, 36, 44, 52, 60]
FS_R = [16, 24, 32, 40, 48, 56]
COC_R = [26, 34, 42, 50, 58]
SC_R = [22, 28, 34, 40, 46, 52, 58]
PM_R = [8, 20, 40, 60]
AI_R = [1, 14, 28, 42, 56]
FA_R = [1, 10, 20]
IA_R = [30, 40, 50, 60]
MA_R = [34, 46, 58]
MS_R = [20, 28, 36, 44, 52, 60]
FW_R = [20, 30, 40, 50, 60]
FRW_R = [22, 32, 42, 52]
DM_R = [12, 24, 36, 48, 60]
AMP_R = [18, 30, 42, 54]
CW_R = [4, 10, 20, 30, 40, 50, 60]
CF_R = [6, 12, 22, 32, 42, 52, 60]
PY_R = [20, 24, 30, 36, 42, 48, 54, 60]
BW_R = [30, 36, 44, 52, 60]
IB_R = [40, 46, 52, 58]

DAMAGE_SPELLS = ["mage_fireball", "mage_frostbolt", "mage_arcane_missiles", "mage_fire_blast", "mage_frost_nova",
                 "mage_arcane_explosion", "mage_blizzard", "mage_flamestrike", "mage_cone_of_cold", "mage_scorch",
                 "mage_pyroblast", "mage_blast_wave"]

CHANNEL_NOTE = "Channel: the effects list is the per-tick payload, applied once per channel tick (channelTicks over castTime)."

abilities = [
    # ------------------------------------------------------------------ Fire
    A("mage_fireball", "Fireball", "fireball", "Fire",
      "Hurls a fiery ball that causes {0} Fire damage and an additional {1} Fire damage over {d1} sec.",
      1, FB_R, castTime=3.5, range=35, cost=mana(30, 410, FB_R), target="Enemy",
      effects=[dmg(14, 22, 561, 715, FB_R, 1.0), apply("mage_fireball_dot")],
      aiHint="Damage", aiPriority=5,
      _note="Ranks 1-4 cast in 1.5/2.0/2.5/3.0 sec in WoW; one castTime per ability, so the 3.5 sec of ranks 5-11 is used. "
            "R11 (60): 561-715 + 72 over 8 sec, 410 mana. The DoT has no spell power coefficient in Classic."),
    A("mage_fire_blast", "Fire Blast", "flame_wave", "Fire", "Blasts the enemy for {0} Fire damage.",
      6, FBL_R, cooldown=8, range=20, cost=mana(40, 340, FBL_R), target="Enemy",
      effects=[dmg(24, 32, 431, 509, FBL_R, 0.429)], aiHint="Damage", aiPriority=6),
    A("mage_scorch", "Scorch", "ember", "Fire", "Scorch the enemy for {0} Fire damage.",
      22, SC_R, castTime=1.5, range=30, cost=mana(50, 150, SC_R), target="Enemy",
      effects=[dmg(56, 69, 237, 280, SC_R, 0.429)], aiHint="Damage", aiPriority=4),
    A("mage_flamestrike", "Flamestrike", "flame_wave", "Fire",
      "Calls down a pillar of fire, burning all enemies within the area for {0} Fire damage and an additional "
      "{1} Fire damage over {d1} sec.",
      16, FS_R, castTime=3, range=30, cost=mana(195, 990, FS_R), target="Point",
      area={"shape": "Circle", "radius": 8, "affects": "Enemies"},
      effects=[dmg(52, 68, 375, 459, FS_R, 0.176), apply("mage_flamestrike_burn")],
      aiHint="AoE", aiPriority=5,
      _note="WoW's lingering fire patch is approximated by a DoT on every enemy hit by the initial blast."),
    # ------------------------------------------------------------------ Frost
    A("mage_frostbolt", "Frostbolt", "ice_shard", "Frost",
      "Launches a bolt of frost at the enemy, causing {0} Frost damage and slowing movement speed by 40% for {d1} sec.",
      4, FROST_R, castTime=3.0, range=30, cost=mana(25, 290, FROST_R), target="Enemy",
      effects=[dmg(18, 20, 515, 555, FROST_R, 0.814), apply("mage_frostbolt_chill")], tags=["Chill"],
      aiHint="Damage", aiPriority=6,
      _note="Ranks 1-4 cast faster (1.5-2.6 sec) and slow for 5-7 sec in WoW; max-rank 3.0 sec cast and 9 sec slow are used. "
            "Coefficient 3.0/3.5 x 0.95 (slow penalty) = 0.814."),
    A("mage_frost_nova", "Frost Nova", "frost_nova", "Frost",
      "Blasts enemies near the caster for {0} Frost damage and freezes them in place for up to {d1} sec. "
      "Damage caused may interrupt the effect.",
      10, FN_R, cooldown=25, cost=mana(55, 145, FN_R), target="Self",
      area={"shape": "Circle", "radius": 10, "centeredOnCaster": True, "affects": "Enemies"},
      effects=[dmg(21, 24, 71, 80, FN_R, 0.043), apply("mage_frost_nova_root")],
      aiHint="CC", aiPriority=6),
    A("mage_cone_of_cold", "Cone of Cold", "frost", "Frost",
      "Targets in a cone in front of the caster take {0} Frost damage and are slowed by 50% for {d1} sec.",
      26, COC_R, cooldown=10, cost=mana(210, 555, COC_R), target="Point", range=10,
      area={"shape": "Cone", "radius": 10, "angle": 90, "centeredOnCaster": True, "affects": "Enemies"},
      effects=[dmg(98, 108, 335, 365, COC_R, 0.136), apply("mage_cone_of_cold_chill")], tags=["Chill"],
      aiHint="AoE", aiPriority=6,
      _note="The Point target only gives the cone its facing; the cone starts at the caster."),
    A("mage_blizzard", "Blizzard", "snowflake", "Frost",
      "Ice shards pelt the target area doing {0} Frost damage every second for 8 sec.",
      20, BZ_R, castTime=8, channeled=True, channelTicks=8, range=30, cost=mana(320, 1400, BZ_R), target="Point",
      area={"shape": "Circle", "radius": 8, "affects": "Enemies"},
      effects=[dmg(25, None, 149, None, BZ_R, 0.095)], aiHint="AoE", aiPriority=5,
      _note=CHANNEL_NOTE + " R6 (60): 1192 damage over 8 sec, 1400 mana. Coefficient 8/3.5 x 1/3 (AoE) spread over 8 ticks."),
    # ------------------------------------------------------------------ Arcane
    A("mage_arcane_missiles", "Arcane Missiles", "missiles", "Arcane",
      "Launches Arcane Missiles at the enemy, causing {0} Arcane damage each second for 5 sec.",
      8, AM_R, castTime=5, channeled=True, channelTicks=5, range=30, cost=mana(85, 655, AM_R), target="Enemy",
      effects=[dmg(14.4, None, 230, None, AM_R, 0.286)], aiHint="Damage", aiPriority=5,
      _note=CHANNEL_NOTE + " WoW rank 1 fires 3 missiles of 24 (72 total) - the per-tick base keeps that total over 5 ticks; "
            "R8 (60, Tome of Arcane Missiles VIII): 5 x 230, 655 mana."),
    A("mage_arcane_explosion", "Arcane Explosion", "arcane_orb", "Arcane",
      "Causes an explosion of arcane magic around the caster, causing {0} Arcane damage to all targets within 10 yards.",
      14, AE_R, cost=mana(75, 390, AE_R), target="Self",
      area={"shape": "Circle", "radius": 10, "centeredOnCaster": True, "affects": "Enemies"},
      effects=[dmg(32, 36, 249, 270, AE_R, 0.143)], aiHint="AoE", aiPriority=4),
    A("mage_blink", "Blink", "blink", "Arcane",
      "Teleports the caster 20 yards forward, unless something is in the way. Also frees the caster from stuns and bonds.",
      20, None, cooldown=15, range=20, cost=mana(85), target="Point",
      effects=[{"type": "Teleport", "target": "Self", "distance": 20},
               {"type": "Special", "target": "Self", "special": "MageBlinkFreedom"}],
      aiHint="Defensive", aiPriority=4,
      _note="On the GCD in Classic. The Point target replaces 'forward': the mage picks the landing spot within 20 yd."),
    A("mage_polymorph", "Polymorph", "sheep", "Arcane",
      "Transforms the enemy into a sheep, forcing it to wander around for up to {d0} sec. While wandering, the sheep "
      "cannot attack or cast spells but will regenerate very quickly. Any damage will transform the target back into "
      "its normal form. Only one target can be polymorphed at a time. Only works on Beasts, Humanoids and Critters.",
      8, PM_R, castTime=1.5, range=30, cost=mana(60, 150, PM_R), target="Enemy",
      requires={"targetCreatureTypes": ["Humanoid", "Beast", "Critter"]},
      effects=[apply("mage_polymorph")], tags=["CC"], aiHint="CC", aiPriority=7,
      _note="Duration 20/30/40/50 sec by rank in WoW; the max-rank 50 sec is used."),
    A("mage_counterspell", "Counterspell", "lock", "Arcane",
      "Counters the enemy's spellcast, preventing any spell from that school of magic from being cast for 10 sec. "
      "Generates a high amount of threat.",
      24, None, time="OffGcd", cooldown=30, range=30, cost=mana(100), target="Enemy",
      effects=[{"type": "Interrupt", "lockout": 10, "threat": 300}], tags=["Interrupt"],
      aiHint="Interrupt", aiPriority=9),
    A("mage_arcane_intellect", "Arcane Intellect", "brain", "Arcane",
      "Increases the target's Intellect by {0} for 30 min.",
      1, AI_R, range=30, cost=mana(60, 1210, AI_R), target="Ally",
      effects=[apply("mage_arcane_intellect", 2, 31, AI_R)], tags=["Buff"], aiHint="Buff", aiPriority=2),
    A("mage_arcane_brilliance", "Arcane Brilliance", "brain", "Arcane",
      "Infuses the caster's party with brilliance, increasing their Intellect by 31 for 1 hour.",
      56, None, cost=mana(1500), target="Self",
      effects=[apply("mage_arcane_brilliance", target="Party")], tags=["Buff"], aiHint="Buff", aiPriority=2,
      _note="Learned from the Tome of Arcane Brilliance in WoW (taught by the trainer here). The Arcane Powder reagent is not modelled."),
    A("mage_mana_shield", "Mana Shield", "shield", "Arcane",
      "Absorbs {0} damage, draining mana instead. Drains 2 mana per damage absorbed. Lasts 1 min.",
      20, MS_R, range=0, cost=mana(110, 530, MS_R), target="Self",
      effects=[apply("mage_mana_shield")], aiHint="Defensive", aiPriority=4),
    A("mage_dampen_magic", "Dampen Magic", "sparkle", "Arcane",
      "Decreases magic damage taken by up to 10 (rank 1) to 90 (rank 5) and healing received by up to 11 to 99. "
      "Lasts 10 min.",
      12, DM_R, range=30, cost=mana(30, 300, DM_R), target="Ally",
      effects=[apply("mage_dampen_magic", 10, 90, DM_R)], tags=["Buff"], aiHint="Buff", aiPriority=1),
    A("mage_amplify_magic", "Amplify Magic", "star", "Arcane",
      "Amplifies magic used against the target, causing it to take up to 15 (rank 1) to 75 (rank 4) additional "
      "damage from spells and receive up to 16 to 83 additional healing. Lasts 10 min.",
      18, AMP_R, range=30, cost=mana(50, 360, AMP_R), target="Ally",
      effects=[apply("mage_amplify_magic", 15, 75, AMP_R)], tags=["Buff"], aiHint="Buff", aiPriority=1),
    A("mage_remove_lesser_curse", "Remove Lesser Curse", "curse", "Arcane",
      "Removes 1 Curse from a friendly target.",
      18, None, range=40, cost=mana(50), target="Ally",
      effects=[{"type": "Dispel", "dispelType": "Curse", "dispelCount": 1}], aiHint="Utility", aiPriority=5),
    A("mage_evocation", "Evocation", "sparkle", "Arcane",
      "While channeling this spell, your mana regeneration is active and increased by 1500%. Lasts 8 sec.",
      20, None, castTime=8, channeled=True, channelTicks=4, cooldown=480, target="Self",
      effects=[{"type": "Special", "target": "Self", "special": "MageEvocation"}],
      aiHint="Utility", aiPriority=3, _note=CHANNEL_NOTE + " One tick per 2 sec mana tick."),
    # ------------------------------------------------------------------ armors & wards
    A("mage_frost_armor", "Frost Armor", "armor", "Frost",
      "Increases Armor by {0}. If an enemy strikes the caster, they may have their movement slowed by 30% and the "
      "time between their attacks increased by 25% for 5 sec. Only one type of Armor spell can be active on the "
      "Mage at any time. Lasts 30 min.",
      1, FA_R, cost=mana(60, 140, FA_R), target="Self",
      effects=[apply("mage_frost_armor", 30, 200, FA_R)], tags=["Armor"], aiHint="Buff", aiPriority=3),
    A("mage_ice_armor", "Ice Armor", "armor", "Frost",
      "Increases Armor by {0} and Frost resistance. If an enemy strikes the caster, they may have their movement "
      "slowed by 30% and the time between their attacks increased by 25% for 5 sec. Only one type of Armor spell "
      "can be active on the Mage at any time. Lasts 30 min.",
      30, IA_R, cost=mana(360, 630, IA_R), target="Self",
      effects=[apply("mage_ice_armor", 290, 560, IA_R)], tags=["Armor"], aiHint="Buff", aiPriority=3),
    A("mage_mage_armor", "Mage Armor", "armor", "Arcane",
      "Increases your resistance to all schools of magic by {0} and allows 30% of your mana regeneration to continue "
      "while casting. Only one type of Armor spell can be active on the Mage at any time. Lasts 30 min.",
      34, MA_R, cost=mana(375, 650, MA_R), target="Self",
      effects=[apply("mage_mage_armor", 5, 15, MA_R)], tags=["Armor"], aiHint="Buff", aiPriority=3),
    A("mage_fire_ward", "Fire Ward", "shield", "Fire",
      "Absorbs {0} Fire damage. Lasts 30 sec.",
      20, FW_R, cooldown=30, cost=mana(85, 380, FW_R), target="Self",
      effects=[apply("mage_fire_ward")], tags=["Ward"], aiHint="Defensive", aiPriority=3),
    A("mage_frost_ward", "Frost Ward", "shield", "Frost",
      "Absorbs {0} Frost damage. Lasts 30 sec.",
      22, FRW_R, cooldown=30, cost=mana(85, 300, FRW_R), target="Self",
      effects=[apply("mage_frost_ward")], tags=["Ward"], aiHint="Defensive", aiPriority=3),
    # ------------------------------------------------------------------ conjuring
    A("mage_conjure_water", "Conjure Water", "drink", "Arcane",
      "Conjures 2 bottles of water, providing the mage and their allies with something to drink.",
      4, CW_R, castTime=3, cost=mana(60, 780, CW_R), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_conjured_water", "count": 2}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0,
      _note="WoW creates a different item per rank (Conjured Water ... Crystal Water); here one item whose drink "
            "scales with the drinker's level (its use ability has scaleWithLevel)."),
    A("mage_conjure_food", "Conjure Food", "food", "Arcane",
      "Conjures 2 loaves of bread, providing the mage and their allies with something to eat.",
      6, CF_R, castTime=3, cost=mana(60, 780, CF_R), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_conjured_food", "count": 2}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0,
      _note="One level-scaled item stands in for Conjured Muffin ... Conjured Cinnamon Roll."),
    A("mage_conjure_mana_agate", "Conjure Mana Agate", "potion_blue", "Arcane",
      "Conjures a mana agate that can be used to instantly restore 375 to 425 mana.",
      28, None, castTime=3, cost=mana(225), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_mana_agate", "count": 1}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0),
    A("mage_conjure_mana_jade", "Conjure Mana Jade", "potion_blue", "Arcane",
      "Conjures a mana jade that can be used to instantly restore 565 to 635 mana.",
      38, None, castTime=3, cost=mana(345), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_mana_jade", "count": 1}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0),
    A("mage_conjure_mana_citrine", "Conjure Mana Citrine", "potion_blue", "Arcane",
      "Conjures a mana citrine that can be used to instantly restore 775 to 825 mana.",
      48, None, castTime=3, cost=mana(470), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_mana_citrine", "count": 1}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0),
    A("mage_conjure_mana_ruby", "Conjure Mana Ruby", "potion_blue", "Arcane",
      "Conjures a mana ruby that can be used to instantly restore 1000 to 1200 mana.",
      58, None, castTime=3, cost=mana(590), target="Self",
      effects=[{"type": "CreateItem", "target": "Self", "item": "mage_mana_ruby", "count": 1}],
      tags=["Conjure"], aiHint="Utility", aiPriority=0),
    # ------------------------------------------------------------------ talent abilities
    A("mage_presence_of_mind", "Presence of Mind", "hourglass", "Arcane",
      "When activated, your next Mage spell with a casting time less than 10 sec becomes an instant cast spell.",
      talent_level(5), None, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
      effects=[apply("mage_presence_of_mind")], aiHint="Buff", aiPriority=6),
    A("mage_arcane_power", "Arcane Power", "arcane", "Arcane",
      "When activated, your spells deal 30% more damage while costing 30% more mana to cast. This effect lasts 15 sec.",
      talent_level(7), None, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
      effects=[apply("mage_arcane_power")], aiHint="Buff", aiPriority=7),
    A("mage_pyroblast", "Pyroblast", "meteor", "Fire",
      "Hurls an immense fiery boulder that causes {0} Fire damage and an additional {1} Fire damage over {d1} sec.",
      20, PY_R, fromTalent=True, castTime=6, range=35, cost=mana(125, 440, PY_R), target="Enemy",
      effects=[dmg(141, 188, 716, 890, PY_R, 1.0), apply("mage_pyroblast_dot")], aiHint="Opener", aiPriority=6,
      _note="R8 (60): 716-890 + 268 over 12 sec, 440 mana."),
    A("mage_blast_wave", "Blast Wave", "flame_wave", "Fire",
      "A wave of flame radiates outward from the caster, damaging all enemies caught within the blast for {0} Fire "
      "damage, and dazing them for {d1} sec.",
      30, BW_R, fromTalent=True, cooldown=45, cost=mana(215, 545, BW_R), target="Self",
      area={"shape": "Circle", "radius": 10, "centeredOnCaster": True, "affects": "Enemies"},
      effects=[dmg(154, 187, 462, 544, BW_R, 0.136), apply("mage_blast_wave_daze")], aiHint="AoE", aiPriority=7),
    A("mage_combustion", "Combustion", "fire", "Fire",
      "When activated, this spell causes each of your Fire damage spell hits to increase your critical strike chance "
      "with Fire damage spells by 10%. This effect lasts until you have caused 3 critical strikes with Fire spells.",
      talent_level(7), None, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
      effects=[apply("mage_combustion")], aiHint="Buff", aiPriority=7),
    A("mage_cold_snap", "Cold Snap", "snowflake", "Frost",
      "When activated, this spell finishes the cooldown on all of your Frost spells.",
      talent_level(3), None, fromTalent=True, time="OffGcd", cooldown=600, target="Self",
      effects=[{"type": "ResetCooldowns", "target": "Self",
                "abilities": ["mage_frost_nova", "mage_cone_of_cold", "mage_frost_ward", "mage_ice_block",
                              "mage_ice_barrier"]}],
      aiHint="Utility", aiPriority=4,
      _note="Explicit ability list so Cold Snap (a Frost spell) does not reset itself."),
    A("mage_ice_block", "Ice Block", "ice_block", "Frost",
      "You become encased in a block of ice, protecting you from all physical attacks and spells for {d0} sec, but "
      "during that time you cannot attack, move or cast spells.",
      talent_level(5), None, fromTalent=True, cooldown=300, target="Self",
      effects=[apply("mage_ice_block")], aiHint="Defensive", aiPriority=8),
    A("mage_ice_barrier", "Ice Barrier", "shield", "Frost",
      "Instantly shields you, absorbing {0} damage. Lasts 1 min. While the shield holds, spells will not be "
      "interrupted.",
      40, IB_R, fromTalent=True, cooldown=30, cost=mana(305, 480, IB_R), target="Self",
      effects=[apply("mage_ice_barrier")], aiHint="Defensive", aiPriority=6),
    # ------------------------------------------------------------------ item uses (hidden)
    A("mage_conjured_water_use", "Drink", "drink", "Arcane",
      "Restores mana over 30 sec. Must remain seated while drinking.",
      4, None, hidden=True, scaleWithLevel=True, target="Self", requires={"notInCombat": True},
      effects=[apply("mage_conjured_water_drink")], aiHint="Utility", aiPriority=0,
      _note="Per tick 15.1 mana at level 4 (Conjured Water: 151 over 18 sec) up to 420 at 60 (Crystal Water: 4200 over 30 sec)."),
    A("mage_conjured_food_use", "Eat", "food", "Arcane",
      "Restores health over 30 sec. Must remain seated while eating.",
      6, None, hidden=True, scaleWithLevel=True, target="Self", requires={"notInCombat": True},
      effects=[apply("mage_conjured_food_eat")], aiHint="Utility", aiPriority=0,
      _note="Per tick 6.1 health at level 6 (Conjured Muffin: 61 over 18 sec) up to 318 at 60 (Conjured Cinnamon Roll: 3180 over 30 sec)."),
]

for gem, lvl, amount, lo, hi in [("agate", 28, 400, 375, 425), ("jade", 38, 600, 565, 635),
                                  ("citrine", 48, 800, 775, 825), ("ruby", 58, 1100, 1000, 1200)]:
    abilities.append(A("mage_mana_%s_use" % gem, "Restore Mana", "potion_blue", "Arcane",
                       "Restores %d to %d mana." % (lo, hi), lvl, None, hidden=True, time="OffGcd",
                       cooldown=120, cooldownGroup="mage_mana_gem", target="Self",
                       effects=[{"type": "GainResource", "target": "Self", "resource": "Mana", "amount": amount}],
                       aiHint="Utility", aiPriority=4,
                       _note="GainResource has no min/max range: the average of %d-%d is used. Gems share a 2 min cooldown." % (lo, hi)))

# ---------------------------------------------------------------------------------------------- auras
auras = [
    AU("mage_fireball_dot", "Fireball", "fireball", "Burning for Fire damage every 2 sec.", "Debuff", "Fire",
       dispel="Magic", duration=8, tickInterval=2, tickEffects=[dmg(1, None, 18, None, FB_R)],
       _note="Per tick (4 ticks): 1 at rank 1 up to 18 (72 total) at rank 11, scaled by the applying Fireball's rank."),
    AU("mage_frostbolt_chill", "Frostbolt", "ice_shard", "Movement slowed by 40%.", "Debuff", "Frost",
       dispel="Magic", duration=9, tags=["Chill"], mods=[mod("MoveSpeed", -40, True)]),
    AU("mage_frost_nova_root", "Frost Nova", "frost_nova", "Frozen in place.", "Debuff", "Frost",
       dispel="Magic", duration=8, tags=["Frozen", "Root"], states=["Root"], breakOnDamage=True,
       _note="WoW: 'damage caused may interrupt the effect' - any damage breaks it here (after the hit that resolves Shatter)."),
    AU("mage_flamestrike_burn", "Flamestrike", "flame_wave", "Burning for Fire damage every 2 sec.", "Debuff", "Fire",
       dispel="None", duration=8, tickInterval=2, tickEffects=[dmg(12, None, 85, None, FS_R, 0.0275)],
       _note="Per tick: 12 (48 over 8 sec) at rank 1 up to 85 (340) at rank 6."),
    AU("mage_cone_of_cold_chill", "Cone of Cold", "frost", "Movement slowed by 50%.", "Debuff", "Frost",
       dispel="Magic", duration=8, tags=["Chill"], mods=[mod("MoveSpeed", -50, True)]),
    AU("mage_polymorph", "Polymorph", "sheep", "Turned into a sheep. Cannot attack or cast; regenerates very quickly.",
       "Debuff", "Arcane", dispel="Magic", duration=50, tags=["Polymorph", "CC"], states=["Polymorph"],
       breakOnDamage=True, tickInterval=1,
       tickEffects=[{"type": "Heal", "pctOfMax": 10},
                    {"type": "GainResource", "resource": "Mana", "pctOfMax": 10}],
       special="MageOnePolymorph"),
    AU("mage_arcane_intellect", "Arcane Intellect", "brain", "Intellect increased.", "Buff", "Arcane",
       dispel="Magic", duration=1800, exclusiveGroup="mage_intellect",
       mods=[mod("Intellect", 2, values=[2, 7, 15, 22, 31])]),
    AU("mage_arcane_brilliance", "Arcane Brilliance", "brain", "Intellect increased by 31.", "Buff", "Arcane",
       dispel="Magic", duration=3600, exclusiveGroup="mage_intellect", mods=[mod("Intellect", 31)]),
    AU("mage_frost_armor", "Frost Armor", "armor", "Armor increased. Melee attackers are chilled.", "Buff", "Frost",
       dispel="Magic", duration=1800, exclusiveGroup="mage_armor", tags=["Armor"],
       mods=[mod("Armor", 30, values=[30, 110, 200])],
       procs=[{"trigger": "OnStruck", "chance": 100,
               "effects": [apply("mage_frost_armor_chill", target="Attacker")]}]),
    AU("mage_ice_armor", "Ice Armor", "armor", "Armor and Frost resistance increased. Melee attackers are chilled.",
       "Buff", "Frost", dispel="Magic", duration=1800, exclusiveGroup="mage_armor", tags=["Armor"],
       mods=[mod("Armor", 290, values=[290, 380, 470, 560]),
             mod("Resistance", 6, school="Frost", values=[6, 9, 12, 15])],
       procs=[{"trigger": "OnStruck", "chance": 100,
               "effects": [apply("mage_frost_armor_chill", target="Attacker")]}]),
    AU("mage_frost_armor_chill", "Chilled", "frost", "Movement slowed by 30% and time between attacks increased by 25%.",
       "Debuff", "Frost", dispel="Magic", duration=5, tags=["Chill"],
       mods=[mod("MoveSpeed", -30, True), mod("MeleeHaste", -25, True)]),
    AU("mage_mage_armor", "Mage Armor", "armor", "Resistance to all schools of magic increased.", "Buff", "Arcane",
       dispel="Magic", duration=1800, exclusiveGroup="mage_armor", tags=["Armor"],
       mods=[mod("Resistance", 5, values=[5, 10, 15]), mod("SpiritRegenWhileCasting", 30)],
       _note="Resistance with no school = all magic schools."),
    AU("mage_mana_shield", "Mana Shield", "shield", "Absorbing damage at the cost of 2 mana per point.", "Buff",
       "Arcane", dispel="Magic", duration=60, exclusiveGroup="mage_mana_shield",
       absorb={"amount": 120, "perLevel": per_level(120, 570, MS_R), "manaPerDamage": 2}),
    AU("mage_fire_ward", "Fire Ward", "shield", "Absorbing Fire damage.", "Buff", "Fire", dispel="Magic", duration=30,
       exclusiveGroup="mage_ward", tags=["Ward"],
       absorb={"amount": 165, "perLevel": per_level(165, 920, FW_R), "schools": ["Fire"]}),
    AU("mage_frost_ward", "Frost Ward", "shield", "Absorbing Frost damage.", "Buff", "Frost", dispel="Magic",
       duration=30, exclusiveGroup="mage_ward", tags=["Ward"],
       absorb={"amount": 165, "perLevel": per_level(165, 675, FRW_R), "schools": ["Frost"]}),
    AU("mage_dampen_magic", "Dampen Magic", "sparkle", "Magic damage and healing taken reduced.", "Buff", "Arcane",
       dispel="Magic", duration=600, exclusiveGroup="mage_dampen_amplify", special="MageFlatMagicModifier"),
    AU("mage_amplify_magic", "Amplify Magic", "star", "Magic damage and healing taken increased.", "Buff", "Arcane",
       dispel="Magic", duration=600, exclusiveGroup="mage_dampen_amplify", special="MageFlatMagicModifier"),
    AU("mage_conjured_water_drink", "Drink", "drink", "Restoring mana.", "Buff", "Arcane", duration=30,
       tags=["Drink"], breakOnDamage=True, breakOnAction=True, breakOnMove=True, tickInterval=3,
       tickEffects=[{"type": "GainResource", "resource": "Mana", "amount": 15.1,
                     "perLevel": per_level(15.1, 420, [4, 60])}]),
    AU("mage_conjured_food_eat", "Food", "food", "Restoring health.", "Buff", "Arcane", duration=30, tags=["Food"],
       breakOnDamage=True, breakOnAction=True, breakOnMove=True, tickInterval=3,
       tickEffects=[heal(6.1, None, 318, None, [6, 60])]),
    # talents
    AU("mage_presence_of_mind", "Presence of Mind", "hourglass",
       "Your next Mage spell with a casting time less than 10 sec is instant.", "Buff", "Arcane", duration=0,
       charges=1, special="MagePresenceOfMind"),
    AU("mage_arcane_power", "Arcane Power", "arcane", "Spells deal 30% more damage and cost 30% more mana.", "Buff",
       "Arcane", dispel="Magic", duration=15, mods=[mod("DamageDone", 30, True), mod("ManaCost", 30, True)]),
    AU("mage_clearcasting", "Clearcasting", "sparkle", "Your next damage spell costs no mana.", "Buff", "Arcane",
       duration=15, charges=1, mods=[mod("ManaCost", -100, True)],
       procs=[{"trigger": "OnSpellCast", "abilities": DAMAGE_SPELLS, "consumeCharge": True, "effects": []}],
       _note="Engine: the ManaCost mod must be applied to the cast that consumes the charge (cost is paid before OnSpellCast fires)."),
    AU("mage_counterspell_silence", "Counterspell - Silenced", "lock", "Silenced.", "Debuff", "Arcane",
       dispel="Magic", duration=4, states=["Silence"]),
    AU("mage_pyroblast_dot", "Pyroblast", "meteor", "Burning for Fire damage every 3 sec.", "Debuff", "Fire",
       dispel="Magic", duration=12, tickInterval=3, tickEffects=[dmg(14, None, 67, None, PY_R, 0.1625)],
       _note="Per tick: 14 (56 over 12 sec) at rank 1 up to 67 (268) at rank 8."),
    AU("mage_blast_wave_daze", "Blast Wave", "flame_wave", "Dazed: movement slowed by 50%.", "Debuff", "Fire",
       dispel="Magic", duration=6, states=["Daze"], mods=[mod("MoveSpeed", -50, True)]),
    AU("mage_combustion", "Combustion", "fire",
       "Each Fire spell hit increases your Fire critical strike chance by 10%. Ends after 3 Fire critical strikes.",
       "Buff", "Fire", dispel="Magic", duration=0, charges=3,
       procs=[{"trigger": "OnSpellHit", "schools": ["Fire"],
               "effects": [apply("mage_combustion_crit", target="Self")]},
              {"trigger": "OnSpellCrit", "schools": ["Fire"], "consumeCharge": True, "effects": []}],
       onRemove=[{"type": "RemoveAura", "target": "Self", "aura": "mage_combustion_crit"}]),
    AU("mage_combustion_crit", "Combustion", "fire", "Fire critical strike chance increased by 10% per stack.", "Buff",
       "Fire", duration=0, maxStacks=20, mods=[mod("SpellCrit", 10, school="Fire")]),
    AU("mage_ice_block", "Ice Block", "ice_block", "Immune to all damage and harmful effects; cannot act or move.",
       "Buff", "Frost", duration=10, states=["Invulnerable", "Root", "Silence", "Pacify"]),
    AU("mage_ice_barrier", "Ice Barrier", "shield", "Absorbing damage.", "Buff", "Frost", dispel="Magic", duration=60,
       absorb={"amount": 438, "perLevel": per_level(438, 818, IB_R), "coef": 0.1}),
    AU("mage_impact_stun", "Impact", "fire", "Stunned.", "Debuff", "Fire", duration=2, states=["Stun"]),
    AU("mage_ignite", "Ignite", "ember", "Burning for Fire damage every 2 sec.", "Debuff", "Fire", duration=4,
       maxStacks=5, tickInterval=2, tickEffects=[{"type": "Damage", "special": "MageIgnite"}],
       special="MageIgnite",
       _note="Tick damage is set by the MageIgnite handler (accumulated pool / remaining ticks), not by min/max."),
    AU("mage_fire_vulnerability", "Fire Vulnerability", "ember", "Fire damage taken increased by 3% per stack.",
       "Debuff", "Fire", duration=30, maxStacks=5, mods=[mod("DamageTaken", 3, True, "Fire")]),
    AU("mage_frostbite", "Frostbite", "frost", "Frozen in place.", "Debuff", "Frost", dispel="Magic", duration=5,
       tags=["Frozen", "Root"], states=["Root"], breakOnDamage=True),
    AU("mage_winters_chill", "Winter's Chill", "snowflake",
       "Chance to be critically hit by Frost spells increased by 2% per stack.", "Debuff", "Frost", duration=15,
       maxStacks=5, special="MageWintersChill"),
]
for i, slow in enumerate([30, 45, 65], 1):
    auras.append(AU("mage_improved_blizzard_chill_%d" % i, "Improved Blizzard", "snowflake",
                    "Movement slowed by %d%%." % slow, "Debuff", "Frost", dispel="Magic", duration=1.5,
                    exclusiveGroup="mage_improved_blizzard", tags=["Chill"], mods=[mod("MoveSpeed", -slow, True)]))


# ---------------------------------------------------------------------------------------------- talents
def T(id, name, icon, tier, col, maxRank, desc, effects, requires=None, note=None):
    t = {"id": id, "name": name, "icon": icon, "tier": tier, "column": col, "maxRank": maxRank, "description": desc,
         "effects": effects}
    if requires:
        t["requires"] = requires
    if note:
        t["_note"] = note
    return t


def stat(stat_, value=0, values=None, pct=False, school=None, target=None):
    p = {"type": "Stat", "stat": stat_}
    if values:
        p["values"] = values
    else:
        p["value"] = value
    if pct:
        p["pct"] = True
    if school:
        p["school"] = school
    if target:
        p["target"] = target
    return p


def amod(prop, value=0, values=None, abilities=None, tags=None, schools=None):
    p = {"type": "AbilityMod", "property": prop}
    if values:
        p["values"] = values
    else:
        p["value"] = value
    if abilities:
        p["abilities"] = abilities
    if tags:
        p["tags"] = tags
    if schools:
        p["schools"] = schools
    return p


def proc(trigger, values, effects, abilities=None, schools=None, tags=None):
    pr = {"trigger": trigger, "chance": values[0], "effects": effects}
    if abilities:
        pr["abilities"] = abilities
    if schools:
        pr["schools"] = schools
    if tags:
        pr["tags"] = tags
    return {"type": "Proc", "values": values, "proc": pr}


def grant(ability):
    return {"type": "GrantAbility", "ability": ability}


def special(name, values=None):
    p = {"type": "Special", "special": name}
    if values:
        p["values"] = values
    return p


arcane = [
    T("mage_arcane_arcane_subtlety", "Arcane Subtlety", "arcane", 1, 0, 2,
      "Reduces your target's resistance to all your spells by {5/10} and reduces the threat caused by your Arcane "
      "spells by {20/40}%.",
      [stat("SpellPenetration", 5), amod("Threat", -20, schools=["Arcane"])]),
    T("mage_arcane_arcane_focus", "Arcane Focus", "eye", 1, 1, 5,
      "Reduces the chance that the opponent can resist your Arcane spells by {2/4/6/8/10}%.",
      [stat("SpellHit", 2, school="Arcane")]),
    T("mage_arcane_improved_arcane_missiles", "Improved Arcane Missiles", "missiles", 1, 2, 5,
      "Gives you a {20/40/60/80/100}% chance to avoid interruption caused by damage while channeling Arcane Missiles.",
      [special("MagePushbackResist", [20, 40, 60, 80, 100])]),
    T("mage_arcane_wand_specialization", "Wand Specialization", "staff", 2, 0, 2,
      "Increases your damage with Wands by {13/25}%.",
      [amod("Damage", values=[13, 25], abilities=["shoot"])]),
    T("mage_arcane_magic_absorption", "Magic Absorption", "sparkle", 2, 1, 5,
      "Increases all resistances by {2/4/6/8/10} and causes all spells you fully resist to restore {1/2/3/4/5}% of "
      "your total mana. 1 sec. cooldown.",
      [stat("Resistance", 2), special("MageMagicAbsorption", [1, 2, 3, 4, 5])]),
    T("mage_arcane_arcane_concentration", "Arcane Concentration", "sparkle", 2, 2, 5,
      "Gives you a {2/4/6/8/10}% chance of entering a Clearcasting state after any damage spell hits a target. The "
      "Clearcasting state reduces the mana cost of your next damage spell by 100%.",
      [proc("OnSpellHit", [2, 4, 6, 8, 10], [apply("mage_clearcasting", target="Self")], abilities=DAMAGE_SPELLS)]),
    T("mage_arcane_magic_attunement", "Magic Attunement", "star", 3, 0, 2,
      "Increases the effect of your Amplify Magic and Dampen Magic spells by {25/50}%.",
      [amod("Effect", values=[25, 50], abilities=["mage_amplify_magic", "mage_dampen_magic"])]),
    T("mage_arcane_improved_arcane_explosion", "Improved Arcane Explosion", "arcane_orb", 3, 1, 3,
      "Increases the critical strike chance of your Arcane Explosion spell by an additional {2/4/6}%.",
      [amod("CritChance", 2, abilities=["mage_arcane_explosion"])]),
    T("mage_arcane_arcane_resilience", "Arcane Resilience", "armor", 3, 2, 1,
      "Increases your armor by an amount equal to 50% of your Intellect.",
      [special("MageArcaneResilience")]),
    T("mage_arcane_improved_mana_shield", "Improved Mana Shield", "shield", 4, 0, 2,
      "Decreases the mana lost per point of damage taken when Mana Shield is active by {10/20}%.",
      [special("MageImprovedManaShield", [10, 20])]),
    T("mage_arcane_improved_counterspell", "Improved Counterspell", "lock", 4, 1, 2,
      "Gives your Counterspell a {50/100}% chance to silence the target for 4 sec.",
      [proc("OnSpellHit", [50, 100], [apply("mage_counterspell_silence")], abilities=["mage_counterspell"])]),
    T("mage_arcane_arcane_meditation", "Arcane Meditation", "brain", 4, 3, 3,
      "Allows {5/10/15}% of your Mana regeneration to continue while casting.",
      [stat("SpiritRegenWhileCasting", 5)]),
    T("mage_arcane_presence_of_mind", "Presence of Mind", "hourglass", 5, 1, 1,
      "When activated, your next Mage spell with a casting time less than 10 sec becomes an instant cast spell. "
      "3 min cooldown.", [grant("mage_presence_of_mind")]),
    T("mage_arcane_arcane_mind", "Arcane Mind", "brain", 5, 2, 5,
      "Increases your maximum Mana by {2/4/6/8/10}%.", [stat("Mana", 2, pct=True)]),
    T("mage_arcane_arcane_instability", "Arcane Instability", "arcane_orb", 6, 1, 3,
      "Increases your spell damage and critical strike chance by {1/2/3}%.",
      [stat("DamageDone", 1, pct=True), stat("SpellCrit", 1)], requires="mage_arcane_presence_of_mind"),
    T("mage_arcane_arcane_power", "Arcane Power", "arcane", 7, 1, 1,
      "When activated, your spells deal 30% more damage while costing 30% more mana to cast. This effect lasts "
      "15 sec. 3 min cooldown.", [grant("mage_arcane_power")], requires="mage_arcane_arcane_instability"),
]

fire = [
    T("mage_fire_improved_fireball", "Improved Fireball", "fireball", 1, 1, 5,
      "Reduces the casting time of your Fireball spell by {0.1/0.2/0.3/0.4/0.5} sec.",
      [amod("CastTime", -0.1, abilities=["mage_fireball"])]),
    T("mage_fire_impact", "Impact", "fire", 1, 2, 5,
      "Gives your Fire spells a {2/4/6/8/10}% chance to stun the target for 2 sec.",
      [proc("OnSpellHit", [2, 4, 6, 8, 10], [apply("mage_impact_stun")], schools=["Fire"])]),
    T("mage_fire_ignite", "Ignite", "ember", 2, 0, 5,
      "Your critical strikes from Fire damage spells cause the target to burn for an additional {8/16/24/32/40}% of "
      "your spell's damage over 4 sec.", [special("MageIgnite", [8, 16, 24, 32, 40])]),
    T("mage_fire_flame_throwing", "Flame Throwing", "flame_wave", 2, 1, 2,
      "Increases the range of your Fire spells by {3/6} yards.",
      [amod("Range", values=[9, 17], abilities=["mage_fireball", "mage_pyroblast"]),
       amod("Range", values=[10, 20], abilities=["mage_scorch", "mage_flamestrike"]),
       amod("Range", values=[15, 30], abilities=["mage_fire_blast"])],
      note="Range mods are percentages: +3/+6 yd on 35 yd (Fireball, Pyroblast), 30 yd (Scorch, Flamestrike), 20 yd (Fire Blast)."),
    T("mage_fire_improved_fire_blast", "Improved Fire Blast", "flame_wave", 2, 2, 3,
      "Reduces the cooldown of your Fire Blast spell by {0.5/1/1.5} sec.",
      [amod("Cooldown", -0.5, abilities=["mage_fire_blast"])]),
    T("mage_fire_incinerate", "Incinerate", "ember", 3, 0, 2,
      "Increases the critical strike chance of your Fire Blast and Scorch spells by {2/4}%.",
      [amod("CritChance", 2, abilities=["mage_fire_blast", "mage_scorch"])]),
    T("mage_fire_improved_flamestrike", "Improved Flamestrike", "flame_wave", 3, 1, 3,
      "Increases the critical strike chance of your Flamestrike spell by {5/10/15}%.",
      [amod("CritChance", 5, abilities=["mage_flamestrike"])]),
    T("mage_fire_pyroblast", "Pyroblast", "meteor", 3, 2, 1,
      "Hurls an immense fiery boulder that causes 141 to 188 Fire damage and an additional 56 Fire damage over "
      "12 sec.", [grant("mage_pyroblast")]),
    T("mage_fire_burning_soul", "Burning Soul", "fire", 3, 3, 2,
      "Gives your Fire spells a {35/70}% chance to not lose casting time when you take damage and reduces the threat "
      "caused by your Fire spells by {15/30}%.",
      [amod("Threat", -15, schools=["Fire"]), special("MagePushbackResist", [35, 70])]),
    T("mage_fire_improved_scorch", "Improved Scorch", "ember", 4, 0, 3,
      "Your Scorch spells have a {33/66/100}% chance to cause your target to be vulnerable to Fire damage. This "
      "vulnerability increases the Fire damage dealt to your target by 3% and lasts 30 sec. Stacks up to 5 times.",
      [proc("OnSpellHit", [33, 66, 100], [apply("mage_fire_vulnerability")], abilities=["mage_scorch"])]),
    T("mage_fire_improved_fire_ward", "Improved Fire Ward", "shield", 4, 1, 2,
      "Causes your Fire Ward to have a {10/20}% chance to reflect Fire spells while active.",
      [special("MageWardReflect", [10, 20])]),
    T("mage_fire_master_of_elements", "Master of Elements", "fire", 4, 3, 3,
      "Your Fire and Frost spell criticals will refund {10/20/30}% of their base mana cost.",
      [special("MageMasterOfElements", [10, 20, 30])]),
    T("mage_fire_critical_mass", "Critical Mass", "fire", 5, 1, 3,
      "Increases the critical strike chance of your Fire spells by {2/4/6}%.",
      [stat("SpellCrit", 2, school="Fire")]),
    T("mage_fire_blast_wave", "Blast Wave", "flame_wave", 5, 2, 1,
      "A wave of flame radiates outward from the caster, damaging all enemies caught within the blast for 154 to "
      "187 Fire damage, and dazing them for 6 sec.", [grant("mage_blast_wave")], requires="mage_fire_pyroblast"),
    T("mage_fire_fire_power", "Fire Power", "fire", 6, 2, 5,
      "Increases the damage done by your Fire spells by {2/4/6/8/10}%.",
      [stat("DamageDone", 2, pct=True, school="Fire")]),
    T("mage_fire_combustion", "Combustion", "fire", 7, 1, 1,
      "When activated, this spell causes each of your Fire damage spell hits to increase your critical strike chance "
      "with Fire damage spells by 10%. This effect lasts until you have caused 3 critical strikes with Fire spells. "
      "3 min cooldown.", [grant("mage_combustion")], requires="mage_fire_critical_mass"),
]

blizz_procs = []
for i in range(3):
    vals = [0, 0, 0]
    vals[i] = 100
    blizz_procs.append(proc("OnSpellHit", vals, [apply("mage_improved_blizzard_chill_%d" % (i + 1))],
                            abilities=["mage_blizzard"]))

frost = [
    T("mage_frost_frost_warding", "Frost Warding", "shield", 1, 0, 2,
      "Increases the armor and resistances given by your Frost Armor and Ice Armor spells by {15/30}%. In addition, "
      "gives your Frost Ward a {10/20}% chance to reflect Frost spells and effects while active.",
      [amod("Effect", 15, abilities=["mage_frost_armor", "mage_ice_armor"]), special("MageWardReflect", [10, 20])]),
    T("mage_frost_improved_frostbolt", "Improved Frostbolt", "ice_shard", 1, 1, 5,
      "Reduces the casting time of your Frostbolt spell by {0.1/0.2/0.3/0.4/0.5} sec.",
      [amod("CastTime", -0.1, abilities=["mage_frostbolt"])]),
    T("mage_frost_elemental_precision", "Elemental Precision", "eye", 1, 2, 3,
      "Reduces the chance that the opponent can resist your Frost and Fire spells by {2/4/6}%.",
      [stat("SpellHit", 2, school="Frost"), stat("SpellHit", 2, school="Fire")]),
    T("mage_frost_ice_shards", "Ice Shards", "ice_shard", 2, 0, 5,
      "Increases the critical strike damage bonus of your Frost spells by {20/40/60/80/100}%.",
      [amod("CritBonus", 20, schools=["Frost"])]),
    T("mage_frost_frostbite", "Frostbite", "frost", 2, 1, 3,
      "Gives your Chill effects a {5/10/15}% chance to freeze the target for 5 sec.",
      [proc("OnSpellHit", [5, 10, 15], [apply("mage_frostbite")],
            abilities=["mage_frostbolt", "mage_cone_of_cold"])],
      note="Chill sources: Frostbolt and Cone of Cold (Blizzard's chill from Improved Blizzard and Frost Armor's chill are not included)."),
    T("mage_frost_improved_frost_nova", "Improved Frost Nova", "frost_nova", 2, 2, 2,
      "Reduces the cooldown of your Frost Nova spell by {2/4} sec.",
      [amod("Cooldown", -2, abilities=["mage_frost_nova"])]),
    T("mage_frost_permafrost", "Permafrost", "snowflake", 2, 3, 3,
      "Increases the duration of your Chill effects by {1/2/3} sec and reduces the target's speed by an additional "
      "{4/7/10}%.",
      [amod("Duration", 1, abilities=["mage_frostbolt", "mage_cone_of_cold"]),
       amod("Effect", values=[10, 17.5, 25], abilities=["mage_frostbolt"]),
       amod("Effect", values=[8, 14, 20], abilities=["mage_cone_of_cold"])],
      note="Effect is a percentage of the slow: 40% -> 44/47/50% (Frostbolt), 50% -> 54/57/60% (Cone of Cold)."),
    T("mage_frost_piercing_ice", "Piercing Ice", "ice_shard", 3, 0, 3,
      "Increases the damage done by your Frost spells by {2/4/6}%.",
      [stat("DamageDone", 2, pct=True, school="Frost")]),
    T("mage_frost_cold_snap", "Cold Snap", "snowflake", 3, 1, 1,
      "When activated, this spell finishes the cooldown on all of your Frost spells. 10 min cooldown.",
      [grant("mage_cold_snap")]),
    T("mage_frost_improved_blizzard", "Improved Blizzard", "snowflake", 3, 3, 3,
      "Adds a chill effect to your Blizzard spell. This effect lowers the target's movement speed by {30/45/65}%. "
      "Lasts 1.5 sec.", blizz_procs,
      note="One proc per rank with one-hot chances so each rank applies its own slow aura (30/45/65%)."),
    T("mage_frost_arctic_reach", "Arctic Reach", "snowflake", 4, 0, 2,
      "Increases the range of your Frostbolt and Blizzard spells and the radius of your Frost Nova and Cone of Cold "
      "spells by {10/20}%.",
      [amod("Range", 10, abilities=["mage_frostbolt", "mage_blizzard"]),
       amod("Radius", 10, abilities=["mage_frost_nova", "mage_cone_of_cold"])]),
    T("mage_frost_frost_channeling", "Frost Channeling", "frost", 4, 1, 3,
      "Reduces the mana cost of your Frost spells by {5/10/15}% and reduces the threat caused by your Frost spells "
      "by {10/20/30}%.",
      [amod("Cost", -5, schools=["Frost"]), amod("Threat", -10, schools=["Frost"])]),
    T("mage_frost_shatter", "Shatter", "ice_shard", 4, 2, 5,
      "Increases the critical strike chance of all your spells against frozen targets by {10/20/30/40/50}%.",
      [special("MageShatter", [10, 20, 30, 40, 50])], requires="mage_frost_improved_frost_nova"),
    T("mage_frost_ice_block", "Ice Block", "ice_block", 5, 1, 1,
      "You become encased in a block of ice, protecting you from all physical attacks and spells for 10 sec, but "
      "during that time you cannot attack, move or cast spells. 5 min cooldown.", [grant("mage_ice_block")]),
    T("mage_frost_improved_cone_of_cold", "Improved Cone of Cold", "frost", 5, 2, 3,
      "Increases the damage dealt by your Cone of Cold spell by {15/25/35}%.",
      [amod("Damage", values=[15, 25, 35], abilities=["mage_cone_of_cold"])]),
    T("mage_frost_winters_chill", "Winter's Chill", "snowflake", 6, 2, 5,
      "Gives your Frost damage spells a {20/40/60/80/100}% chance to apply the Winter's Chill effect, which increases "
      "the chance a Frost spell will critically hit the target by 2% for 15 sec. Stacks up to 5 times.",
      [proc("OnSpellHit", [20, 40, 60, 80, 100], [apply("mage_winters_chill")], schools=["Frost"])]),
    T("mage_frost_ice_barrier", "Ice Barrier", "shield", 7, 1, 1,
      "Instantly shields you, absorbing 438 damage. Lasts 1 min. While the shield holds, spells will not be "
      "interrupted. 30 sec cooldown.", [grant("mage_ice_barrier")], requires="mage_frost_ice_block"),
]

trees = [
    {"id": "tree_mage_arcane", "classId": "Mage", "name": "Arcane", "icon": "arcane",
     "description": "Manipulate raw arcane energy: mana efficiency, Clearcasting, Presence of Mind and Arcane Power.",
     "talents": arcane},
    {"id": "tree_mage_fire", "classId": "Mage", "name": "Fire", "icon": "fire",
     "description": "Ignite enemies with Fire: big crits, Ignite, Pyroblast, Blast Wave and Combustion.",
     "talents": fire},
    {"id": "tree_mage_frost", "classId": "Mage", "name": "Frost", "icon": "frost",
     "description": "Freeze and shatter: slows, roots, Ice Block, Ice Barrier and devastating Shatter combos.",
     "talents": frost},
]


def rep(tid, n):
    return [tid] * n


default_build = (
    rep("mage_frost_improved_frostbolt", 5) + rep("mage_frost_elemental_precision", 3) +
    rep("mage_frost_ice_shards", 5) + rep("mage_frost_improved_frost_nova", 2) + ["mage_frost_cold_snap"] +
    rep("mage_frost_piercing_ice", 3) + ["mage_frost_improved_blizzard"] + rep("mage_frost_frost_channeling", 3) +
    rep("mage_frost_shatter", 5) + ["mage_frost_ice_block", "mage_frost_improved_cone_of_cold",
                                    "mage_frost_ice_barrier"] +
    rep("mage_frost_improved_cone_of_cold", 2) + ["mage_frost_arctic_reach"] +
    rep("mage_arcane_arcane_subtlety", 2) + rep("mage_arcane_arcane_focus", 3) +
    rep("mage_arcane_arcane_concentration", 5) + rep("mage_arcane_improved_arcane_explosion", 3) +
    rep("mage_arcane_magic_absorption", 2) + rep("mage_arcane_arcane_meditation", 2)
)
assert len(default_build) == 51, len(default_build)

# ---------------------------------------------------------------------------------------------- items
items = [
    {"id": "mage_starter_staff", "name": "Worn Staff", "icon": "staff",
     "description": "A plain ash staff, smoothed by a hundred apprentices' hands.", "kind": "Weapon",
     "quality": "Common", "itemLevel": 1, "equip": "TwoHand", "weaponType": "Staff", "minDamage": 2, "maxDamage": 4,
     "speed": 2.9, "price": 9, "classes": ["Mage"]},
    {"id": "mage_starter_robe", "name": "Apprentice's Robe", "icon": "armor",
     "description": "Blue wool robe embroidered with the sigil of the Lanternvale college.", "kind": "Armor",
     "quality": "Common", "itemLevel": 1, "equip": "Chest", "armorType": "Cloth", "armor": 3, "price": 5,
     "classes": ["Mage"]},
    {"id": "mage_starter_pants", "name": "Apprentice's Pants", "icon": "armor",
     "description": "Simple cloth trousers, ink-stained at the knees.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "equip": "Legs", "armorType": "Cloth", "armor": 2, "price": 4, "classes": ["Mage"]},
    {"id": "mage_starter_boots", "name": "Apprentice's Boots", "icon": "boot",
     "description": "Soft boots made for library floors rather than forest trails.", "kind": "Armor",
     "quality": "Common", "itemLevel": 1, "equip": "Feet", "armorType": "Cloth", "armor": 1, "price": 3,
     "classes": ["Mage"]},
    {"id": "mage_conjured_water", "name": "Conjured Water", "icon": "drink",
     "description": "Cold, clear and faintly sparkling. Conjured items vanish after a long rest.", "kind": "Drink",
     "quality": "Common", "itemLevel": 5, "use": "mage_conjured_water_use", "consumable": True, "stack": 20,
     "price": 0},
    {"id": "mage_conjured_food", "name": "Conjured Bread", "icon": "food",
     "description": "Warm bread that smells of cinnamon and ozone. Conjured items vanish after a long rest.",
     "kind": "Food", "quality": "Common", "itemLevel": 5, "use": "mage_conjured_food_use", "consumable": True,
     "stack": 20, "price": 0},
]
for gem, name, lvl, ilvl in [("agate", "Mana Agate", 28, 30), ("jade", "Mana Jade", 38, 40),
                             ("citrine", "Mana Citrine", 48, 50), ("ruby", "Mana Ruby", 58, 60)]:
    items.append({"id": "mage_mana_%s" % gem, "name": name, "icon": "potion_blue",
                  "description": "A conjured gem humming with stored mana.", "kind": "Consumable",
                  "quality": "Common", "itemLevel": ilvl, "use": "mage_mana_%s_use" % gem, "consumable": True,
                  "stack": 1, "unique": True, "price": 0, "classes": ["Mage"]})

# ---------------------------------------------------------------------------------------------- class
mage_class = {
    "id": "Mage", "name": "Mage",
    "description": "Scholars of the arcane who bend fire, frost and raw magic to their will. Fragile in cloth, a mage "
                   "wins by never being reached: slowing, freezing and sheeping foes while unleashing devastating spells.",
    "roles": ["Ranged DPS", "Support"], "color": "#69CCF0", "icon": "crest_mage", "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth"], "armorUpgrade": {"level": 40, "type": "None"},
    "weaponTypes": ["Staff", "OneHandSword", "Dagger", "Wand"], "dualWieldLevel": 0, "canParry": False,
    "canBlock": False,
    "baseStatsLevel1": {"strength": 20, "agility": 20, "stamina": 20, "intellect": 23, "spirit": 23},
    "baseStatsLevel60": {"strength": 34, "agility": 38, "stamina": 46, "intellect": 123, "spirit": 125},
    "baseHealthLevel1": 32, "baseHealthLevel60": 1360, "baseManaLevel1": 100, "baseManaLevel60": 1213,
    "meleeAp": "Strength", "rangedAp": "Strength",
    "agilityPerMeleeCritAt60": 20, "intellectPerSpellCritAt60": 59.5, "agilityPerDodgeAt60": 20,
    "baseMeleeCrit": 3.2, "baseSpellCrit": 0.2, "baseDodge": 3.2,
    "manaRegenBase": 12.5, "manaRegenPerSpirit": 0.25,
    "basicAttack": "attack",
    "startingAbilities": ["mage_fireball", "mage_frost_armor", "shoot"],
    "startingItems": ["mage_starter_staff", "mage_starter_robe", "mage_starter_pants", "mage_starter_boots"],
    "startingStance": "",
    "talentTrees": ["tree_mage_arcane", "tree_mage_fire", "tree_mage_frost"],
    "defaultBuild": default_build,
    "sprite": "char_mage", "portrait": "portrait_mage",
    "designNotes": "The mage is a glass cannon with the best crowd control in the valley: Frostbolt and Frost Nova "
                   "keep enemies at range, Polymorph takes one out of the fight and Counterspell shuts down casters. "
                   "Mana is the real health bar - drink conjured water between fights, Evocate when dry and watch "
                   "your threat. Frost is the forgiving levelling path (Shatter combos, Ice Block, Ice Barrier); "
                   "Fire hits hardest; Arcane stretches every drop of mana.",
    "_note": "Base stats/health/mana: naked Human Mage (WoW Classic 1.12). Mana regen per 2 sec tick = 12.5 + Spirit/4. "
             "Spell crit: 0.2% base + 1% per 59.5 Intellect at 60.",
}

SPECIALS = [
    {"id": "MageBlinkFreedom", "usedBy": "mage_blink",
     "behaviour": "Resolves after the Teleport. Removes from the caster every aura that imposes the Stun or Root "
                  "state (Frost Nova, nets, roots, stuns), regardless of dispel type, except Invulnerable/Banish "
                  "auras. Blink is otherwise an ordinary instant on the GCD; it cannot be used while the caster is "
                  "Stunned because stunned units lose their turn."},
    {"id": "MageOnePolymorph", "usedBy": "mage_polymorph (aura)",
     "behaviour": "Only one target can be polymorphed per mage. When mage_polymorph is applied by caster C, remove "
                  "any other mage_polymorph aura that C applied to a different unit. Polymorph regeneration is "
                  "data-driven (tick: +10% max health and mana per second)."},
    {"id": "MageEvocation", "usedBy": "mage_evocation",
     "behaviour": "Each of the 4 channel ticks (one per 2 sec) grants the caster mana = 16 x (manaRegenBase + "
                  "Spirit x manaRegenPerSpirit) of the caster's class, i.e. normal Spirit regen plus 1500%, "
                  "ignoring the five-second rule (MP5 is not multiplied). Ticks interrupted by Interrupt/stun/"
                  "silence are lost. Does not count as spending mana."},
    {"id": "MageFlatMagicModifier", "usedBy": "mage_dampen_magic, mage_amplify_magic (auras)",
     "behaviour": "Values by the aura's rank (rank of the applying ability) - Dampen Magic ranks 1-5: damage "
                  "D = 10/20/40/60/90, healing H = 11/22/44/66/99; Amplify Magic ranks 1-4: D = 15/30/50/75, "
                  "H = 16/33/55/83. Both are multiplied by the aura's EffectMult (AbilityMod Effect of the caster, "
                  "i.e. Magic Attunement +25/50%). Dampen Magic: every incoming non-Physical damage hit (direct or "
                  "periodic) on the holder is reduced by D (minimum 0) and every incoming heal by H (minimum 0). "
                  "Amplify Magic: every incoming non-Physical damage hit is increased by D and every incoming heal "
                  "by H. Applied after crit, before absorbs and resistance."},
    {"id": "MagePresenceOfMind", "usedBy": "mage_presence_of_mind (aura)",
     "behaviour": "While the aura (1 charge) is present, the next Mage ability (classId Mage) the holder starts "
                  "whose effective castTime is > 0 and < 10 sec and which is not channeled is cast instantly: "
                  "castTime becomes 0 so its Time cost is the GCD (or 0 when OffGcd) and it resolves immediately. "
                  "The aura is removed when that cast starts. Channels (Arcane Missiles, Blizzard, Evocation) do "
                  "not consume it."},
    {"id": "MageIgnite", "usedBy": "mage_fire_ignite (talent), mage_ignite (aura)",
     "behaviour": "Talent rank r gives P = values[r-1] (8/16/24/32/40%). When the mage lands a critical strike with "
                  "a Fire damage effect (direct damage of a Fire ability; not periodic ticks, not Ignite itself), "
                  "add P% of the final crit damage dealt to an Ignite pool on that target for that mage and "
                  "apply/refresh mage_ignite (duration 4 sec, 2 ticks at 2 sec). If the aura already exists, add a "
                  "stack (max 5) and refresh duration; extra damage keeps accumulating in the pool even at 5 stacks "
                  "(1.12 'rolling ignite'). Each tick deals pool / remaining ticks Fire damage (removing it from the "
                  "pool), cannot crit, ignores spell power, and is credited to the mage (threat included). The pool "
                  "is cleared when the aura ends."},
    {"id": "MageShatter", "usedBy": "mage_frost_shatter (talent)",
     "behaviour": "For each ability with a Damage effect cast by the mage, if the target (each target for area "
                  "spells) has an aura tagged \"Frozen\" (mage_frost_nova_root, mage_frostbite or any other aura "
                  "with that tag) at the moment the crit roll is made, add values[rank-1] (10..50) percentage points "
                  "to the spell's crit chance. The crit roll happens before damage is applied, so the hit that "
                  "breaks the freeze still benefits."},
    {"id": "MageWintersChill", "usedBy": "mage_winters_chill (aura)",
     "behaviour": "While the debuff is on a unit, every Frost-school damage effect cast against that unit (by any "
                  "caster) gains +2 percentage points of crit chance per stack (max 5 stacks = +10%). Reapplying "
                  "adds a stack and refreshes the 15 sec duration."},
    {"id": "MageMasterOfElements", "usedBy": "mage_fire_master_of_elements (talent)",
     "behaviour": "When one of the mage's Fire or Frost damage effects critically hits, the mage gains mana equal to "
                  "values[rank-1]% (10/20/30) of the base mana cost of that ability at the caster's rank (cost.amount "
                  "+ cost.perLevel x (rankLevel - learnLevel), before talents/auras modify it). Once per cast even "
                  "if an area spell crits several targets."},
    {"id": "MageWardReflect", "usedBy": "mage_fire_improved_fire_ward, mage_frost_frost_warding (talents)",
     "behaviour": "Improved Fire Ward: while mage_fire_ward is active on the mage, each hostile Fire-school ability "
                  "targeting the mage has values[rank-1]% (10/20) chance to be reflected: the ability's effects "
                  "resolve against the original caster instead (with the original caster's numbers); the ward "
                  "absorbs nothing in that case. Frost Warding: same for Frost-school abilities while "
                  "mage_frost_ward is active (10/20%). Area spells and periodic ticks cannot be reflected."},
    {"id": "MageMagicAbsorption", "usedBy": "mage_arcane_magic_absorption (talent)",
     "behaviour": "When a hostile non-Physical ability fully misses the mage because of the spell hit roll "
                  "(a resist), the mage gains values[rank-1]% (1..5) of maximum mana. Internal cooldown 1 sec. "
                  "(The +2/rank all-resistance part is a plain Stat passive.)"},
    {"id": "MageArcaneResilience", "usedBy": "mage_arcane_arcane_resilience (talent)",
     "behaviour": "The mage's Armor stat is increased by 50% of current total Intellect (recomputed whenever "
                  "Intellect changes)."},
    {"id": "MageImprovedManaShield", "usedBy": "mage_arcane_improved_mana_shield (talent)",
     "behaviour": "While the mage has mage_mana_shield, its absorb.manaPerDamage is multiplied by "
                  "(1 - values[rank-1]/100): 2 -> 1.8 / 1.6 mana per point absorbed."},
    {"id": "MagePushbackResist",
     "usedBy": "mage_arcane_improved_arcane_missiles, mage_fire_burning_soul (talents)",
     "behaviour": "Lanternvale has no casting pushback by default. If the engine implements damage pushback "
                  "(recommended: each damaging hit taken while a cast/channel is pending delays a cast by 0.5 sec "
                  "or removes 0.5 sec of remaining channel time, at most twice per pending cast), these talents give "
                  "values[rank-1]% chance to ignore each pushback: Improved Arcane Missiles for mage_arcane_missiles "
                  "(20..100%), Burning Soul for Fire-school abilities (35/70%). Without pushback they do nothing "
                  "beyond Burning Soul's threat reduction (data)."},
]

BUNDLE = {
    "classes": [mage_class],
    "abilities": abilities,
    "auras": auras,
    "talentTrees": trees,
    "items": items,
    "specials": SPECIALS,
}
