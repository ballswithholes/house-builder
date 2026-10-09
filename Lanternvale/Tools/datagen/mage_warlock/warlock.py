from mwcommon import *
from mage import T, stat, amod, proc, grant, special, rep

CID = "Warlock"


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


def P(id, name, icon, school, desc, learn, **kw):
    """Demon ability: hidden, scales with the demon's level (WoW ranks come from Grimoires)."""
    return A(id, name, icon, school, desc, learn, None, hidden=True, scaleWithLevel=True, **kw)


def AU(id, name, icon, desc, kind="Buff", school="Shadow", **kw):
    a = {"id": id, "name": name, "icon": icon, "description": desc, "kind": kind, "school": school}
    a.update(kw)
    return a


CURSE = {"dispel": "Curse", "exclusiveGroup": "warlock_curse", "exclusivePerCaster": True}
CHANNEL_NOTE = "Channel: the effects list is the per-tick payload, applied once per channel tick (channelTicks over castTime)."
SHARD = "WarlockConsumeSoulShard"

# --------------------------------------------------------------------------- rank tables (WoW Classic 1.12)
SB_R = [1, 6, 12, 20, 28, 36, 44, 52, 60]          # level-60 rank uses Grimoire of Shadow Bolt X values (AQ)
IMM_R = [1, 10, 20, 30, 40, 50, 60]                # level-60 rank uses Grimoire of Immolate VIII values (AQ)
COR_R = [4, 14, 24, 34, 44, 54, 60]                # R7 = Grimoire of Corruption VII (AQ)
COA_R = [8, 18, 28, 38, 48, 58]
COW_R = [4, 12, 22, 32, 42, 52]
CORK_R = [14, 28, 42, 56]
COT_R = [26, 50]
COE_R = [32, 46, 60]
COS_R = [44, 56]
LT_R = [6, 16, 26, 36, 46, 56]
DL_R = [14, 22, 30, 38, 46, 54]
DS_R = [10, 24, 38, 52]
DM_R = [24, 34, 44, 52]
HF_R = [12, 20, 28, 36, 44, 52, 60]
FEAR_R = [8, 32, 56]
HOWL_R = [40, 54]
DC_R = [42, 50, 58]
SP_R = [18, 26, 34, 42, 50, 58]
ROF_R = [20, 34, 46, 58]
HELL_R = [30, 42, 54]
SW_R = [32, 42, 52, 60]
DSK_R = [1, 10]
DA_R = [20, 30, 40, 50, 60]
BAN_R = [28, 48]
ENS_R = [30, 44, 56]
SF_R = [48, 56]
SL_R = [30, 38, 48, 58]
DP_R = [40, 50, 60]
SBURN_R = [20, 24, 32, 40, 48, 56]
CONF_R = [40, 48, 54, 60]

SUMMONS = ["warlock_summon_imp", "warlock_summon_voidwalker", "warlock_summon_succubus", "warlock_summon_felhunter"]
CLEAR_SAC = {"type": "RemoveAura", "target": "Self", "auraTag": "DemonicSacrifice"}

abilities = [
    # ------------------------------------------------------------------ Destruction
    A("warlock_shadow_bolt", "Shadow Bolt", "shadow", "Shadow", "Sends a shadowy bolt at the enemy, causing {0} Shadow damage.",
      1, SB_R, castTime=3.0, range=30, cost=mana(25, 380, SB_R), target="Enemy",
      effects=[dmg(13, 18, 482, 538, SB_R, 0.857)], tags=["Destruction"], aiHint="Damage", aiPriority=5,
      _note="Ranks 1-3 cast in 1.7/2.2/2.8 sec in WoW; the 3.0 sec of rank 4+ is used. Level-60 values = rank 10 "
            "(482-538, 380 mana)."),
    A("warlock_immolate", "Immolate", "fire", "Fire",
      "Burns the enemy for {0} Fire damage and then an additional {1} Fire damage over {d1} sec.",
      1, IMM_R, castTime=2.0, range=30, cost=mana(25, 380, IMM_R), target="Enemy",
      effects=[dmg(11, None, 279, None, IMM_R, 0.2), apply("warlock_immolate")], tags=["Destruction"],
      aiHint="Damage", aiPriority=6,
      _note="Level-60 values = rank 8: 279 + 510 over 15 sec, 380 mana. Coefficients 0.20 direct / 0.65 over the DoT."),
    A("warlock_searing_pain", "Searing Pain", "ember", "Fire",
      "Inflict searing pain on the enemy target, causing {0} Fire damage. Causes a high amount of threat.",
      18, SP_R, castTime=1.5, range=30, cost=mana(45, 168, SP_R), target="Enemy",
      effects=[dmg(38, 47, 208, 244, SP_R, 0.429)], tags=["Destruction"], special="WarlockSearingPainThreat",
      aiHint="Damage", aiPriority=3),
    A("warlock_rain_of_fire", "Rain of Fire", "meteor", "Fire",
      "Calls down a fiery rain to burn enemies in the area of effect for {0} Fire damage every 2 sec for 8 sec.",
      20, ROF_R, castTime=8, channeled=True, channelTicks=4, range=30, cost=mana(295, 1185, ROF_R),
      target="Point", area={"shape": "Circle", "radius": 8, "affects": "Enemies"},
      effects=[dmg(60, None, 310, None, ROF_R, 0.19)], tags=["Destruction"], aiHint="AoE", aiPriority=5,
      _note=CHANNEL_NOTE + " R4 (58): 1240 over 8 sec, 1185 mana."),
    A("warlock_hellfire", "Hellfire", "flame_wave", "Fire",
      "Ignites the area surrounding the caster, causing {1} Fire damage to herself and {0} Fire damage to all nearby "
      "enemies every 1 sec. Lasts 15 sec.",
      30, HELL_R, castTime=15, channeled=True, channelTicks=15, cost=mana(645, 1300, HELL_R), target="Self",
      area={"shape": "Circle", "radius": 10, "centeredOnCaster": True, "affects": "Enemies"},
      effects=[dmg(83, None, 208, None, HELL_R, 0.095),
               dmg(83, None, 208, None, HELL_R, 0, target="Self", cannotCrit=True)],
      tags=["Destruction"], aiHint="AoE", aiPriority=3, _note=CHANNEL_NOTE),
    A("warlock_soul_fire", "Soul Fire", "fire", "Fire", "Burn the enemy's soul, causing {0} Fire damage.",
      48, SF_R, castTime=6, cooldown=60, range=30, cost=mana(305, 335, SF_R), target="Enemy",
      effects=[dmg(640, 801, 703, 881, SF_R, 1.0)], tags=["Destruction"], special=SHARD,
      aiHint="Opener", aiPriority=4, _note="Reagent: 1 soul_shard (WarlockConsumeSoulShard)."),
    # ------------------------------------------------------------------ Affliction
    A("warlock_corruption", "Corruption", "skull", "Shadow",
      "Corrupts the target, causing {0} Shadow damage over {d0} sec.",
      4, COR_R, castTime=2.0, range=30, cost=mana(35, 340, COR_R), target="Enemy",
      effects=[apply("warlock_corruption")], tags=["Affliction"], aiHint="Damage", aiPriority=7),
    A("warlock_curse_of_agony", "Curse of Agony", "curse", "Shadow",
      "Curses the target with agony, causing {0} Shadow damage over {d0} sec. This damage is dealt slowly at first, and "
      "builds up as the Curse reaches its full duration. Only one Curse per Warlock can be active on any one target.",
      8, COA_R, range=30, cost=mana(25, 215, COA_R), target="Enemy",
      effects=[apply("warlock_curse_of_agony")], tags=["Affliction", "Curse"], aiHint="Damage", aiPriority=6),
    A("warlock_curse_of_weakness", "Curse of Weakness", "curse", "Shadow",
      "Target's attack power is reduced by {0} for 2 min. Only one Curse per Warlock can be active on any one target.",
      4, COW_R, range=30, cost=mana(20, 200, COW_R), target="Enemy",
      effects=[apply("warlock_curse_of_weakness", 3, 90, COW_R)], tags=["Affliction", "Curse"],
      aiHint="Debuff", aiPriority=2),
    A("warlock_curse_of_recklessness", "Curse of Recklessness", "curse", "Shadow",
      "Reduces the target's armor by {0} but increases its attack power. Lasts 2 min. Only one Curse per Warlock "
      "can be active on any one target.",
      14, CORK_R, range=30, cost=mana(35, 160, CORK_R), target="Enemy",
      effects=[apply("warlock_curse_of_recklessness", 140, 640, CORK_R)], tags=["Affliction", "Curse"],
      aiHint="Debuff", aiPriority=2,
      _note="Rank 1: -140 armor / +20 AP ... rank 4: -640 armor / +90 AP (per-rank aura mod values)."),
    A("warlock_curse_of_tongues", "Curse of Tongues", "curse", "Shadow",
      "Forces the target to speak in Demonic, increasing the casting time of all spells by {0}%. Only one Curse per "
      "Warlock can be active on any one target. Lasts 30 sec.",
      26, COT_R, range=30, cost=mana(80, 110, COT_R), target="Enemy",
      effects=[apply("warlock_curse_of_tongues", 50, 60, COT_R)], tags=["Affliction", "Curse"],
      aiHint="Debuff", aiPriority=3),
    A("warlock_curse_of_the_elements", "Curse of the Elements", "curse", "Shadow",
      "Curses the target for 5 min, reducing Fire and Frost resistances by {0} and increasing Fire and Frost damage "
      "taken. Only one Curse per Warlock can be active on any one target.",
      32, COE_R, range=30, cost=mana(100, 200, COE_R), target="Enemy",
      effects=[apply("warlock_curse_of_the_elements", 45, 75, COE_R)], tags=["Affliction", "Curse"],
      aiHint="Debuff", aiPriority=3, _note="Rank 1: -45 res / +6% damage taken; rank 3: -75 / +10%."),
    A("warlock_curse_of_shadow", "Curse of Shadow", "curse", "Shadow",
      "Curses the target for 5 min, reducing Shadow and Arcane resistances by {0} and increasing Shadow and Arcane "
      "damage taken. Only one Curse per Warlock can be active on any one target.",
      44, COS_R, range=30, cost=mana(150, 200, COS_R), target="Enemy",
      effects=[apply("warlock_curse_of_shadow", 60, 75, COS_R)], tags=["Affliction", "Curse"],
      aiHint="Debuff", aiPriority=4, _note="Rank 1: -60 res / +8%; rank 2: -75 / +10%."),
    A("warlock_curse_of_doom", "Curse of Doom", "curse", "Shadow",
      "Curses the target with impending doom, causing 3200 Shadow damage after 1 min. If the target dies from this "
      "damage, there is a chance that a Doomguard will be summoned. Cannot be cast on players.",
      60, None, cooldown=60, range=30, cost=mana(300), target="Enemy",
      effects=[apply("warlock_curse_of_doom")], tags=["Affliction", "Curse"], aiHint="Damage", aiPriority=3,
      _note="The Doomguard summon is not modelled."),
    A("warlock_life_tap", "Life Tap", "blood", "Shadow",
      "Converts {0} health into {0} mana.",
      6, LT_R, target="Self",
      effects=[{"type": "Special", "target": "Self", "special": "WarlockLifeTap", **scaled(20, 424, LT_R),
                "coef": 0.8}],
      tags=["Affliction"], aiHint="Utility", aiPriority=3,
      _note="20 at rank 1 up to 424 at rank 6; +80% of Shadow spell damage."),
    A("warlock_drain_life", "Drain Life", "drain", "Shadow",
      "Transfers {0} health every second from the target to the caster. Lasts 5 sec.",
      14, DL_R, castTime=5, channeled=True, channelTicks=5, range=20, cost=mana(55, 300, DL_R), target="Enemy",
      effects=[dmg(10, None, 71, None, DL_R, 0.143, pctOfDamage=100)], tags=["Affliction"],
      aiHint="Damage", aiPriority=4, _note=CHANNEL_NOTE),
    A("warlock_drain_soul", "Drain Soul", "soul_shard", "Shadow",
      "Drains the soul of the target, causing {0} Shadow damage every 3 sec for 15 sec. If the target dies while "
      "being drained, the caster gains a Soul Shard.",
      10, DS_R, castTime=15, channeled=True, channelTicks=5, range=30, cost=mana(55, 290, DS_R), target="Enemy",
      effects=[dmg(11, None, 91, None, DS_R, 0.2)], tags=["Affliction"], special="WarlockDrainSoulShard",
      aiHint="Finisher", aiPriority=4,
      _note=CHANNEL_NOTE + " R4 (52): 455 over 15 sec. AI: use on targets below ~25% health to farm shards."),
    A("warlock_drain_mana", "Drain Mana", "drain", "Shadow",
      "Transfers {0} mana every second from the target to the caster. Lasts 5 sec.",
      24, DM_R, castTime=5, channeled=True, channelTicks=5, range=20, cost=mana(95, 230, DM_R), target="Enemy",
      effects=[{"type": "DrainResource", "resource": "Mana", "pctToCaster": 100, "amount": 17,
                "perLevel": per_level(17, 64, DM_R)}],
      tags=["Affliction"], aiHint="Utility", aiPriority=2, _note=CHANNEL_NOTE),
    A("warlock_fear", "Fear", "fear", "Shadow",
      "Strikes fear in the enemy, causing it to run in fear for up to {d0} sec. Damage caused may interrupt the "
      "effect. Only 1 target can be feared at a time.",
      8, FEAR_R, castTime=1.5, range=20, cost=mana(40, 80, FEAR_R), target="Enemy",
      effects=[apply("warlock_fear")], tags=["Affliction", "CC"], aiHint="CC", aiPriority=6,
      _note="10/15/20 sec by rank in WoW; the max-rank 20 sec is used."),
    A("warlock_howl_of_terror", "Howl of Terror", "fear", "Shadow",
      "Howl, causing 5 enemies within 10 yds to flee in terror for {d0} sec. Damage caused may interrupt the effect.",
      40, HOWL_R, castTime=2.0, cooldown=40, cost=mana(160, 200, HOWL_R), target="Self",
      area={"shape": "Circle", "radius": 10, "centeredOnCaster": True, "maxTargets": 5, "affects": "Enemies"},
      effects=[apply("warlock_howl_of_terror")], tags=["Affliction", "CC"], aiHint="CC", aiPriority=6),
    A("warlock_death_coil", "Death Coil", "skull", "Shadow",
      "Causes the enemy target to run in horror for 3 sec and causes {0} Shadow damage. The caster gains 100% of "
      "the damage caused in health.",
      42, DC_R, cooldown=120, range=30, cost=mana(430, 565, DC_R), target="Enemy",
      effects=[dmg(301, None, 476, None, DC_R, 0.214, pctOfDamage=100), apply("warlock_death_coil")],
      tags=["Affliction", "CC"], aiHint="Defensive", aiPriority=7),
    # ------------------------------------------------------------------ Demonology
    A("warlock_demon_skin", "Demon Skin", "armor", "Shadow",
      "Protects the caster, increasing armor by {0} and health regeneration. Only one type of Armor spell can be "
      "active on the Warlock at any time. Lasts 30 min.",
      1, DSK_R, cost=mana(50, 120, DSK_R), target="Self",
      effects=[apply("warlock_demon_skin", 90, 190, DSK_R)], tags=["Demonology", "Armor"], aiHint="Buff",
      aiPriority=3),
    A("warlock_demon_armor", "Demon Armor", "armor", "Shadow",
      "Protects the caster, increasing armor by {0}, Shadow resistance and health regeneration. Only one type of "
      "Armor spell can be active on the Warlock at any time. Lasts 30 min.",
      20, DA_R, cost=mana(110, 580, DA_R), target="Self",
      effects=[apply("warlock_demon_armor", 210, 570, DA_R)], tags=["Demonology", "Armor"], aiHint="Buff",
      aiPriority=3),
    A("warlock_shadow_ward", "Shadow Ward", "shield", "Shadow",
      "Absorbs {0} Shadow damage. Lasts 30 sec.",
      32, SW_R, cooldown=30, cost=mana(135, 310, SW_R), target="Self",
      effects=[apply("warlock_shadow_ward")], tags=["Demonology", "Ward"], aiHint="Defensive", aiPriority=3),
    A("warlock_health_funnel", "Health Funnel", "heart", "Shadow",
      "Gives {0} health to the caster's pet every second for 10 sec as long as the caster gives {1} health every "
      "second.",
      12, HF_R, castTime=10, channeled=True, channelTicks=10, range=20, target="Pet",
      effects=[heal(12, None, 153, None, HF_R),
               {"type": "DrainResource", "target": "Self", "resource": "Health", "amount": 13,
                "perLevel": per_level(13, 172, HF_R)}],
      tags=["Demonology", "Heal"], requires={"hasPet": True}, aiHint="Heal", aiPriority=4, _note=CHANNEL_NOTE),
    A("warlock_banish", "Banish", "void", "Shadow",
      "Banishes the enemy target, preventing all action but making it invulnerable for up to {d0} sec. Only one "
      "target can be banished at a time. Only works on Demons and Elementals.",
      28, BAN_R, castTime=1.5, range=30, cost=mana(100, 200, BAN_R), target="Enemy",
      requires={"targetCreatureTypes": ["Demon", "Elemental"]},
      effects=[apply("warlock_banish")], tags=["Demonology", "CC"], aiHint="CC", aiPriority=6,
      _note="20/30 sec by rank; max rank used."),
    A("warlock_enslave_demon", "Enslave Demon", "demon", "Shadow",
      "Enslaves the target demon (up to level 45/55/60 by rank), forcing it to do your bidding. While enslaved, the "
      "time between the demon's attacks is increased by 30% and its casting speed is slowed by 20%. Lasts up to "
      "5 min.",
      30, ENS_R, castTime=3, range=30, cost=mana(400, 1000, ENS_R), target="Enemy",
      requires={"targetCreatureTypes": ["Demon"]},
      effects=[apply("warlock_enslave_demon")], tags=["Demonology", "CC"], special="WarlockEnslaveDemon",
      aiHint="CC", aiPriority=2),
    A("warlock_summon_imp", "Summon Imp", "imp", "Shadow",
      "Summons an Imp under the command of the Warlock.",
      1, None, castTime=10, cost={"type": "Mana", "pctBaseMana": 64}, target="Self",
      effects=[{"type": "Summon", "target": "Self", "summon": "warlock_demon_imp", "lifetime": -1}, CLEAR_SAC],
      tags=["Demonology", "Pet", "Summon"], aiHint="Summon", aiPriority=8,
      _note="One demon at a time: summoning replaces the current pet. Also cancels Demonic Sacrifice buffs."),
    A("warlock_summon_voidwalker", "Summon Voidwalker", "void", "Shadow",
      "Summons a Voidwalker under the command of the Warlock.",
      10, None, castTime=10, cost={"type": "Mana", "pctBaseMana": 80}, target="Self",
      effects=[{"type": "Summon", "target": "Self", "summon": "warlock_demon_voidwalker", "lifetime": -1},
               CLEAR_SAC],
      tags=["Demonology", "Pet", "Summon"], special=SHARD, aiHint="Summon", aiPriority=8,
      _note="Reagent: 1 soul_shard."),
    A("warlock_summon_succubus", "Summon Succubus", "demon", "Shadow",
      "Summons a Succubus under the command of the Warlock.",
      20, None, castTime=10, cost={"type": "Mana", "pctBaseMana": 80}, target="Self",
      effects=[{"type": "Summon", "target": "Self", "summon": "warlock_demon_succubus", "lifetime": -1},
               CLEAR_SAC],
      tags=["Demonology", "Pet", "Summon"], special=SHARD, aiHint="Summon", aiPriority=7,
      _note="Reagent: 1 soul_shard."),
    A("warlock_summon_felhunter", "Summon Felhunter", "demon", "Shadow",
      "Summons a Felhunter under the command of the Warlock.",
      30, None, castTime=10, cost={"type": "Mana", "pctBaseMana": 80}, target="Self",
      effects=[{"type": "Summon", "target": "Self", "summon": "warlock_demon_felhunter", "lifetime": -1},
               CLEAR_SAC],
      tags=["Demonology", "Pet", "Summon"], special=SHARD, aiHint="Summon", aiPriority=7,
      _note="Reagent: 1 soul_shard."),
    A("warlock_inferno", "Inferno", "meteor", "Fire",
      "Summons a meteor from the Twisting Nether, causing {0} Fire damage and stunning all enemy targets in the "
      "area for 2 sec. An Infernal rises from the crater, under the command of the caster for 5 min.",
      50, None, castTime=2, cooldown=3600, range=30, cost=mana(1180), target="Point",
      area={"shape": "Circle", "radius": 8, "affects": "Enemies"},
      effects=[dmg(200, None), apply("warlock_inferno_stun"),
               {"type": "Summon", "target": "Self", "summon": "warlock_demon_infernal", "lifetime": 300}],
      tags=["Demonology", "Summon"], aiHint="Summon", aiPriority=3,
      _note="Infernal Stone reagent and the 'control lost after 5 min' turn-hostile behaviour are not modelled: "
            "the Infernal takes the pet slot and despawns after 300 sec."),
]

# ---------------------------------------------------------------- stones (one spell + item per tier, as in WoW)
HEALTHSTONES = [("minor", "Minor Healthstone", 10, 100, 95), ("lesser", "Lesser Healthstone", 22, 250, 240),
                ("normal", "Healthstone", 34, 500, 475), ("greater", "Greater Healthstone", 46, 800, 750),
                ("major", "Major Healthstone", 58, 1200, 1120)]
SOULSTONES = [("minor", "Minor Soulstone", 18, 400, 700, 135), ("lesser", "Lesser Soulstone", 30, 750, 1200, 285),
              ("normal", "Soulstone", 40, 1100, 1700, 500), ("greater", "Greater Soulstone", 50, 1600, 2200, 750),
              ("major", "Major Soulstone", 60, 2200, 2800, 1000)]
SPELLSTONES = [("normal", "Spellstone", 36, 400, 500), ("greater", "Greater Spellstone", 48, 650, 750),
               ("major", "Major Spellstone", 60, 900, 1000)]
FIRESTONES = [("lesser", "Lesser Firestone", 28, 10, 500), ("normal", "Firestone", 36, 15, 700),
              ("greater", "Greater Firestone", 46, 20, 900), ("major", "Major Firestone", 56, 25, 1100)]


def tier_name(t, base):
    return base if t == "normal" else "%s (%s)" % (base, t.capitalize())


HS_USES, SPS_USES, FIRE_FLAMES = [], [], []
items = [
    {"id": "warlock_starter_dagger", "name": "Worn Dagger", "icon": "dagger",
     "description": "A nicked ritual knife. Its edge is dull; its owner is not.", "kind": "Weapon",
     "quality": "Common", "itemLevel": 1, "equip": "OneHand", "weaponType": "Dagger", "minDamage": 1, "maxDamage": 3,
     "speed": 1.6, "price": 7, "classes": ["Warlock"]},
    {"id": "warlock_starter_robe", "name": "Acolyte's Robe", "icon": "armor",
     "description": "Black cloth stitched with faint violet runes.", "kind": "Armor", "quality": "Common",
     "itemLevel": 1, "equip": "Chest", "armorType": "Cloth", "armor": 3, "price": 5, "classes": ["Warlock"]},
    {"id": "warlock_starter_pants", "name": "Acolyte's Pants", "icon": "armor",
     "description": "Plain trousers singed at the hem.", "kind": "Armor", "quality": "Common", "itemLevel": 1,
     "equip": "Legs", "armorType": "Cloth", "armor": 2, "price": 4, "classes": ["Warlock"]},
    {"id": "warlock_starter_shoes", "name": "Acolyte's Shoes", "icon": "boot",
     "description": "Quiet shoes for quiet rituals.", "kind": "Armor", "quality": "Common", "itemLevel": 1,
     "equip": "Feet", "armorType": "Cloth", "armor": 1, "price": 3, "classes": ["Warlock"]},
    {"id": "soul_shard", "name": "Soul Shard", "icon": "soul_shard",
     "description": "A violet crystal holding a captured soul. Warlocks spend them to summon demons, craft stones and "
                    "cast Shadowburn or Soul Fire. A warlock's bags hold at most 32.",
     "kind": "Reagent", "quality": "Common", "itemLevel": 1, "stack": 32, "price": 0},
]

for t, name, lvl, hp, cost in HEALTHSTONES:
    use_id = "warlock_healthstone_%s_use" % t
    HS_USES.append(use_id)
    abilities.append(A("warlock_create_healthstone_%s" % t, tier_name(t, "Create Healthstone"), "heart", "Shadow",
                       "Creates a %s that can be used to instantly restore %d health. Requires a Soul Shard." % (name, hp),
                       lvl, None, castTime=3, cost=mana(cost), target="Self",
                       effects=[{"type": "CreateItem", "target": "Self", "item": "warlock_healthstone_%s" % t,
                                 "count": 1}],
                       tags=["Demonology", "Conjure"], special=SHARD, aiHint="Utility", aiPriority=0))
    abilities.append(A(use_id, "Healthstone", "heart", "Shadow", "Instantly restores {0} health.", lvl, None,
                       hidden=True, time="OffGcd", cooldown=120, cooldownGroup="warlock_healthstone", target="Self",
                       effects=[heal(hp, None, target="Self")], aiHint="Heal", aiPriority=7))
    items.append({"id": "warlock_healthstone_%s" % t, "name": name, "icon": "heart",
                  "description": "A green stone pulsing with stolen vitality. Use: instantly restores %d health." % hp,
                  "kind": "Consumable", "quality": "Common", "itemLevel": lvl, "use": use_id, "consumable": True,
                  "stack": 1, "unique": True, "price": 0})

for t, name, lvl, hp, mp, cost in SOULSTONES:
    use_id = "warlock_soulstone_%s_use" % t
    abilities.append(A("warlock_create_soulstone_%s" % t, tier_name(t, "Create Soulstone"), "soul_shard", "Shadow",
                       "Creates a %s. The stone can be used to store a friendly target's soul; if the target falls "
                       "they may rise again with %d health and %d mana. Requires a Soul Shard." % (name, hp, mp),
                       lvl, None, castTime=3, cost=mana(cost), target="Self",
                       effects=[{"type": "CreateItem", "target": "Self", "item": "warlock_soulstone_%s" % t,
                                 "count": 1}],
                       tags=["Demonology", "Conjure"], special=SHARD, aiHint="Utility", aiPriority=0))
    abilities.append(A(use_id, "Soulstone Resurrection", "soul_shard", "Shadow",
                       "Stores the friendly target's soul for 30 min. If the target is downed while the soul is "
                       "stored, they may rise with %d health and %d mana." % (hp, mp),
                       lvl, None, hidden=True, castTime=3, cooldown=1800, cooldownGroup="warlock_soulstone",
                       range=30, target="Ally", effects=[apply("warlock_soulstone_%s" % t)],
                       aiHint="Buff", aiPriority=2))
    items.append({"id": "warlock_soulstone_%s" % t, "name": name, "icon": "soul_shard",
                  "description": "A violet stone that can hold a soul in safekeeping.", "kind": "Consumable",
                  "quality": "Common", "itemLevel": lvl, "use": use_id, "consumable": True, "stack": 1,
                  "unique": True, "price": 0})

for t, name, lvl, absorb_amt, cost in SPELLSTONES:
    use_id = "warlock_spellstone_%s_use" % t
    SPS_USES.append(use_id)
    abilities.append(A("warlock_create_spellstone_%s" % t, tier_name(t, "Create Spellstone"), "sparkle", "Shadow",
                       "Creates a %s that can be used to absorb %d magical damage. Requires a Soul Shard." % (name, absorb_amt),
                       lvl, None, castTime=5, cost=mana(cost), target="Self",
                       effects=[{"type": "CreateItem", "target": "Self", "item": "warlock_spellstone_%s" % t,
                                 "count": 1}],
                       tags=["Demonology", "Conjure"], special=SHARD, aiHint="Utility", aiPriority=0))
    abilities.append(A(use_id, "Spellstone", "sparkle", "Shadow",
                       "Absorbs %d magical damage. Lasts 1 min." % absorb_amt, lvl, None, hidden=True,
                       time="OffGcd", cooldown=180, cooldownGroup="warlock_spellstone", target="Self",
                       effects=[apply("warlock_spellstone_%s" % t)], aiHint="Defensive", aiPriority=5))
    items.append({"id": "warlock_spellstone_%s" % t, "name": name, "icon": "sparkle",
                  "description": "A smooth blue stone that drinks hostile magic.", "kind": "Consumable",
                  "quality": "Common", "itemLevel": lvl, "use": use_id, "consumable": True, "stack": 1,
                  "unique": True, "price": 0})

for t, name, lvl, fire, cost in FIRESTONES:
    flames = "warlock_firestone_%s_flames" % t
    FIRE_FLAMES.append(flames)
    abilities.append(A("warlock_create_firestone_%s" % t, tier_name(t, "Create Firestone"), "ember", "Fire",
                       "Creates a %s. When held in the off hand, your melee attacks deal %d additional Fire damage. "
                       "Requires a Soul Shard." % (name, fire),
                       lvl, None, castTime=3, cost=mana(cost), target="Self",
                       effects=[{"type": "CreateItem", "target": "Self", "item": "warlock_firestone_%s" % t,
                                 "count": 1}],
                       tags=["Demonology", "Conjure"], special=SHARD, aiHint="Utility", aiPriority=0))
    abilities.append(A(flames, "Firestone", "ember", "Fire", "Deals {0} additional Fire damage.", lvl, None,
                       hidden=True, time="OffGcd", target="Enemy", melee=True, breaksStealth=True,
                       effects=[dmg(fire, None)], aiHint="Damage", aiPriority=0,
                       _note="Triggered by the Firestone's equip proc on each melee hit; exists so Improved Firestone can modify it."))
    items.append({"id": "warlock_firestone_%s" % t, "name": name, "icon": "ember",
                  "description": "A warm red stone, held in the off hand.", "kind": "Weapon", "quality": "Common",
                  "itemLevel": lvl, "requiredLevel": lvl, "equip": "OffHand", "weaponType": "HeldInOffhand",
                  "equipEffects": [{"type": "Proc", "proc": {"trigger": "OnMeleeHit", "chance": 100, "effects": [
                      {"type": "TriggerAbility", "ability": flames}]}}],
                  "stack": 1, "unique": True, "price": 0, "classes": ["Warlock"]})

# ---------------------------------------------------------------- talent-granted abilities
abilities += [
    A("warlock_curse_of_exhaustion", "Curse of Exhaustion", "curse", "Shadow",
      "Reduces the target's movement speed by 10% for 12 sec. Only one Curse per Warlock can be active on any one "
      "target.",
      talent_level(5), None, fromTalent=True, range=30, cost=mana(156), target="Enemy",
      effects=[apply("warlock_curse_of_exhaustion")], tags=["Affliction", "Curse"], aiHint="Debuff", aiPriority=3),
    A("warlock_siphon_life", "Siphon Life", "drain", "Shadow",
      "Transfers {0} health from the target to the caster over {d0} sec.",
      30, SL_R, fromTalent=True, range=30, cost=mana(150, 410, SL_R), target="Enemy",
      effects=[apply("warlock_siphon_life")], tags=["Affliction"], aiHint="Damage", aiPriority=6),
    A("warlock_amplify_curse", "Amplify Curse", "curse", "Shadow",
      "Increases the effect of your next Curse of Weakness or Curse of Agony by 50%, or your next Curse of "
      "Exhaustion by 20%. Lasts 30 sec.",
      talent_level(3), None, fromTalent=True, time="OffGcd", cooldown=180, target="Self",
      effects=[apply("warlock_amplify_curse")], tags=["Affliction"], aiHint="Buff", aiPriority=5),
    A("warlock_dark_pact", "Dark Pact", "moon", "Shadow",
      "Drains {0} of your pet's Mana, returning 100% to you.",
      40, DP_R, fromTalent=True, target="Self", requires={"hasPet": True},
      effects=[{"type": "DrainResource", "target": "Pet", "resource": "Mana", "pctToCaster": 100, "amount": 150,
                "perLevel": per_level(150, 250, DP_R)}],
      tags=["Affliction"], aiHint="Utility", aiPriority=3),
    A("warlock_fel_domination", "Fel Domination", "demon", "Shadow",
      "Your next Imp, Voidwalker, Succubus, or Felhunter Summon spell has its casting time reduced by 5.5 sec and "
      "its Mana cost reduced by 50%.",
      talent_level(3), None, fromTalent=True, time="OffGcd", cooldown=900, target="Self",
      effects=[apply("warlock_fel_domination")], tags=["Demonology"], aiHint="Summon", aiPriority=6),
    A("warlock_demonic_sacrifice", "Demonic Sacrifice", "skull", "Shadow",
      "When activated, sacrifices your summoned demon to grant you an effect that lasts 30 min. The effect is "
      "canceled if any Demon is summoned. Imp: Increases your Fire damage by 15%. Voidwalker: Restores 3% of total "
      "Health every 4 sec. Succubus: Increases your Shadow damage by 15%. Felhunter: Restores 2% of total Mana "
      "every 4 sec.",
      talent_level(5), None, fromTalent=True, target="Self", requires={"hasPet": True},
      effects=[{"type": "Special", "target": "Self", "special": "WarlockDemonicSacrifice"}], tags=["Demonology"],
      aiHint="Buff", aiPriority=2),
    A("warlock_soul_link", "Soul Link", "demon", "Shadow",
      "When active, 30% of all damage taken by the caster is taken by your Imp, Voidwalker, Succubus, or Felhunter "
      "demon instead. In addition, both the demon and master will inflict 3% more damage. Lasts as long as the demon "
      "is active.",
      talent_level(7), None, fromTalent=True, cost={"type": "Mana", "pctBaseMana": 16}, target="Self",
      requires={"hasPet": True},
      effects=[apply("warlock_soul_link"), apply("warlock_soul_link_pet", target="Pet")], tags=["Demonology"],
      aiHint="Buff", aiPriority=5),
    A("warlock_shadowburn", "Shadowburn", "shadow", "Shadow",
      "Instantly blasts the target for {0} Shadow damage. If the target dies within 5 sec of Shadowburn, and yields "
      "experience or honor, the caster gains a Soul Shard.",
      20, SBURN_R, fromTalent=True, cooldown=15, range=20, cost=mana(105, 365, SBURN_R), target="Enemy",
      effects=[dmg(91, 104, 462, 514, SBURN_R, 0.429), apply("warlock_shadowburn")], tags=["Destruction"],
      special=SHARD, aiHint="Finisher", aiPriority=5, _note="Reagent: 1 soul_shard."),
    A("warlock_conflagrate", "Conflagrate", "fire", "Fire",
      "Ignites a target that is already afflicted by your Immolate, dealing {0} Fire damage and consuming the "
      "Immolate spell.",
      40, CONF_R, fromTalent=True, cooldown=10, range=30, cost=mana(165, 280, CONF_R), target="Enemy",
      requires={"targetAuras": ["warlock_immolate"], "targetAuraFromSelf": True},
      effects=[dmg(249, 316, 447, 557, CONF_R, 0.429),
               {"type": "RemoveAura", "aura": "warlock_immolate"}],
      tags=["Destruction"], aiHint="Damage", aiPriority=7),
]

# ---------------------------------------------------------------- demon abilities (hidden, scale with demon level)
FB_IMP = [1, 8, 18, 28, 38, 48, 58]
abilities += [
    P("warlock_imp_firebolt", "Firebolt", "fireball", "Fire", "Deals {0} Fire damage to a target.", 1,
      castTime=2.0, range=30, cost=mana(10, 115, FB_IMP), target="Enemy",
      effects=[dmg(7, 10, 83, 101, FB_IMP, 0.571)], aiHint="Damage", aiPriority=5),
    P("warlock_imp_blood_pact", "Blood Pact", "blood", "Shadow",
      "Increases all party members' Stamina (3 at level 4 up to 42 at level 50). Only party members within 30 "
      "yards are affected.", 4,
      target="Self", effects=[apply("warlock_imp_blood_pact", 3, 42, [4, 14, 26, 38, 50])], tags=["Aura", "Buff"],
      aiHint="Buff", aiPriority=9, _note="Autocast toggle aura; the Imp AI re-applies it when missing."),
    P("warlock_imp_fire_shield", "Fire Shield", "shield", "Fire",
      "Places a Fire Shield on a party member, causing Fire damage (3 at level 14 up to 20 at level 54) to any "
      "enemy that strikes them in melee. Lasts 3 min.", 14, range=30, cost=mana(55, 135, [14, 54]), target="Ally",
      effects=[apply("warlock_imp_fire_shield", 3, 20, [14, 24, 34, 44, 54])], tags=["Buff"], aiHint="Buff",
      aiPriority=3),
    P("warlock_imp_phase_shift", "Phase Shift", "void", "Shadow",
      "Enter phase-shift, preventing all attacks and spells from affecting the Imp, but the Imp cannot attack or "
      "cast spells. Lasts until cancelled (any action).", 12, target="Self",
      effects=[apply("warlock_imp_phase_shift")], aiHint="Defensive", aiPriority=1),
    P("warlock_voidwalker_torment", "Torment", "shout", "Shadow",
      "Torments the target, increasing the Voidwalker's threat by {0}.", 10, cooldown=5, melee=True,
      cost=mana(25, 100, [10, 60]), target="Enemy",
      effects=[{"type": "Threat", "threat": 45, "perLevel": per_level(45, 395, [10, 60])}], tags=["Taunt"],
      aiHint="Taunt", aiPriority=7, _note="Classic Torment adds flat threat (45 -> 395); it does not force the target."),
    P("warlock_voidwalker_sacrifice", "Sacrifice", "shield", "Shadow",
      "Sacrifices the Voidwalker, giving its owner a shield that will absorb {0} damage for 30 sec. While the shield "
      "holds, spellcasting will not be interrupted by damage.", 16, target="Self",
      effects=[apply("warlock_voidwalker_sacrifice", target="Owner"), {"type": "Kill", "target": "Self"}],
      aiHint="Defensive", aiPriority=2, _note="Absorb 305 at level 16 up to 1905 at 56 (aura absorb.perLevel)."),
    P("warlock_voidwalker_suffering", "Suffering", "shout", "Shadow",
      "Increases the Voidwalker's threat by {0} against all enemies within 10 yards.", 24, cooldown=120,
      cost=mana(100, 300, [24, 60]), target="Self",
      effects=[{"type": "Threat", "target": "EnemiesInRadius", "radius": 10, "threat": 150,
                "perLevel": per_level(150, 600, [24, 60])}], tags=["Taunt"], aiHint="Taunt", aiPriority=6),
    P("warlock_voidwalker_consume_shadows", "Consume Shadows", "shadow", "Shadow",
      "Restores {0} health to the Voidwalker every 2 sec for 10 sec.", 18, castTime=10, channeled=True,
      channelTicks=5, target="Self", effects=[heal(18, None, 112, None, [18, 58], target="Self")],
      aiHint="Heal", aiPriority=3, _note=CHANNEL_NOTE),
    P("warlock_succubus_lash_of_pain", "Lash of Pain", "tentacle", "Shadow",
      "An instant attack that lashes the target, causing {0} Shadow damage.", 20, cooldown=12, range=30,
      cost=mana(65, 190, [20, 60]), target="Enemy", effects=[dmg(33, None, 99, None, [20, 60], 0.429)],
      aiHint="Damage", aiPriority=5),
    P("warlock_succubus_seduction", "Seduction", "heart", "Shadow",
      "Seduces the target, preventing all actions for up to {d0} sec. Any damage caused will remove the effect. "
      "Only works against Humanoids.", 26, castTime=1.5, range=20, cost=mana(150), target="Enemy",
      requires={"targetCreatureTypes": ["Humanoid"]}, effects=[apply("warlock_succubus_seduction")], tags=["CC"],
      aiHint="CC", aiPriority=6),
    P("warlock_succubus_soothing_kiss", "Soothing Kiss", "heart", "Shadow",
      "Soothes the target, reducing the Succubus' threat against it.", 22, cooldown=4, range=30,
      cost=mana(100, 250, [22, 58]), target="Enemy",
      effects=[{"type": "Threat", "threat": -45, "perLevel": per_level(-45, -340, [22, 58])}], aiHint="Utility",
      aiPriority=2),
    P("warlock_succubus_lesser_invisibility", "Lesser Invisibility", "stealth", "Shadow",
      "Gives the Succubus Lesser Invisibility for 5 min.", 32, cost=mana(100), target="Self",
      effects=[apply("warlock_succubus_lesser_invisibility")], aiHint="Utility", aiPriority=0),
    P("warlock_felhunter_devour_magic", "Devour Magic", "fang", "Shadow",
      "Purges 1 harmful magic effect from a friend or 1 beneficial magic effect from an enemy. If an effect is "
      "devoured, the Felhunter is healed for {1}.", 30, cooldown=8, range=30, cost=mana(50, 125, [30, 54]),
      target="Any",
      effects=[{"type": "Dispel", "dispelType": "Magic", "dispelCount": 1},
               heal(179, None, 590, None, [30, 38, 46, 54], target="Self")],
      aiHint="Utility", aiPriority=5, _note="The heal is applied even when nothing was devoured (no conditional effect in the schema)."),
    P("warlock_felhunter_spell_lock", "Spell Lock", "lock", "Shadow",
      "Silences the enemy for {d1} sec. If used on a casting target, it will counter the enemy's spellcast, "
      "preventing any spell from that school of magic from being cast for 8 sec.", 36, time="OffGcd",
      cooldown=24, range=30, cost=mana(100), target="Enemy",
      effects=[{"type": "Interrupt", "lockout": 8}, apply("warlock_felhunter_spell_lock")], tags=["Interrupt"],
      aiHint="Interrupt", aiPriority=9, _note="Rank 2 values (4 sec silence, 8 sec lockout)."),
    P("warlock_felhunter_tainted_blood", "Tainted Blood", "blood", "Shadow",
      "Causes Shadow damage (8 at level 32 up to 26 at level 56) to melee attackers for 10 sec.", 32, cooldown=30, cost=mana(60, 160, [32, 56]),
      target="Self", effects=[apply("warlock_felhunter_tainted_blood", 8, 26, [32, 40, 48, 56])],
      aiHint="Defensive", aiPriority=3),
    P("warlock_felhunter_paranoia", "Paranoia", "eye", "Shadow",
      "Increases the stealth detection of the Felhunter's party within 30 yards.", 42, target="Self",
      effects=[apply("warlock_felhunter_paranoia")], tags=["Aura", "Buff"], aiHint="Buff", aiPriority=9,
      _note="Autocast toggle aura."),
]

# ---------------------------------------------------------------------------------------------- auras
def ramp_note():
    return "Average per tick: 7 at rank 1 up to 87 (1044 total) at rank 6; WarlockCurseOfAgonyRamp shapes the 12 ticks."


auras = [
    AU("warlock_immolate", "Immolate", "fire", "Burning for Fire damage every 3 sec.", "Debuff", "Fire",
       dispel="Magic", duration=15, tickInterval=3, tickEffects=[dmg(4, None, 102, None, IMM_R, 0.13)],
       _note="Per tick: 4 (20 over 15 sec) at rank 1 up to 102 (510) at the level-60 rank."),
    AU("warlock_corruption", "Corruption", "skull", "Suffering Shadow damage every 3 sec.", "Debuff", "Shadow",
       dispel="Magic", duration=18, tickInterval=3, tickEffects=[dmg(6.67, None, 137, None, COR_R, 0.156)],
       _note="Rank 1 is 40 over 12 sec in WoW (same total kept over 6 ticks); R7 (60): 822 over 18 sec."),
    AU("warlock_curse_of_agony", "Curse of Agony", "curse", "Suffering increasing Shadow damage every 2 sec.",
       "Debuff", "Shadow", duration=24, tags=["Curse"], tickInterval=2,
       tickEffects=[dmg(7, None, 87, None, COA_R, 0.1)], special="WarlockCurseOfAgonyRamp", _note=ramp_note(), **CURSE),
    AU("warlock_curse_of_weakness", "Curse of Weakness", "curse", "Attack power reduced.", "Debuff", "Shadow",
       duration=120, tags=["Curse"], mods=[mod("AttackPower", -3, values=[-3, -10, -21, -31, -46, -90])], **CURSE),
    AU("warlock_curse_of_recklessness", "Curse of Recklessness", "curse", "Armor reduced, attack power increased.",
       "Debuff", "Shadow", duration=120, tags=["Curse"],
       mods=[mod("Armor", -140, values=[-140, -290, -465, -640]), mod("AttackPower", 20, values=[20, 45, 65, 90])],
       **CURSE),
    AU("warlock_curse_of_tongues", "Curse of Tongues", "curse", "Casting time increased by 50%.", "Debuff",
       "Shadow", duration=30, tags=["Curse"], mods=[mod("CastSpeed", -50, True, values=[-50, -60])],
       _note="CastSpeed -50 is meant as cast time x1.5 (x1.6 at rank 2).", **CURSE),
    AU("warlock_curse_of_the_elements", "Curse of the Elements", "curse",
       "Fire and Frost resistances reduced, Fire and Frost damage taken increased.", "Debuff", "Shadow", duration=300,
       tags=["Curse"],
       mods=[mod("Resistance", -45, school="Fire", values=[-45, -60, -75]),
             mod("Resistance", -45, school="Frost", values=[-45, -60, -75]),
             mod("DamageTaken", 6, True, "Fire", values=[6, 8, 10]),
             mod("DamageTaken", 6, True, "Frost", values=[6, 8, 10])], **CURSE),
    AU("warlock_curse_of_shadow", "Curse of Shadow", "curse",
       "Shadow and Arcane resistances reduced, Shadow and Arcane damage taken increased.", "Debuff", "Shadow",
       duration=300, tags=["Curse"],
       mods=[mod("Resistance", -60, school="Shadow", values=[-60, -75]),
             mod("Resistance", -60, school="Arcane", values=[-60, -75]),
             mod("DamageTaken", 8, True, "Shadow", values=[8, 10]),
             mod("DamageTaken", 8, True, "Arcane", values=[8, 10])], **CURSE),
    AU("warlock_curse_of_doom", "Curse of Doom", "curse", "Will suffer 3200 Shadow damage when the curse expires.",
       "Debuff", "Shadow", duration=60, tags=["Curse"], onExpire=[dmg(3200, None, coef=2.0)], **CURSE),
    AU("warlock_curse_of_exhaustion", "Curse of Exhaustion", "curse", "Movement speed reduced by 10%.", "Debuff",
       "Shadow", duration=12, tags=["Curse"], mods=[mod("MoveSpeed", -10, True)], **CURSE),
    AU("warlock_fear", "Fear", "fear", "Running in fear.", "Debuff", "Shadow", dispel="Magic", duration=20,
       tags=["CC", "Fear"], states=["Fear"], breakOnDamage=True, special="WarlockOneTargetCC"),
    AU("warlock_howl_of_terror", "Howl of Terror", "fear", "Fleeing in terror.", "Debuff", "Shadow", dispel="Magic",
       duration=15, tags=["CC", "Fear"], states=["Fear"], breakOnDamage=True),
    AU("warlock_death_coil", "Death Coil", "skull", "Running in horror.", "Debuff", "Shadow", dispel="Magic",
       duration=3, tags=["CC", "Horror"], states=["Fear"]),
    AU("warlock_demon_skin", "Demon Skin", "armor", "Armor and health regeneration increased.", "Buff", "Shadow",
       dispel="Magic", duration=1800, exclusiveGroup="warlock_armor", tags=["Armor"],
       mods=[mod("Armor", 90, values=[90, 190]), mod("HealthRegen", 5, values=[5, 7])],
       _note="HealthRegen is per 5 sec (WoW: +2/+3 health per 2 sec regen tick)."),
    AU("warlock_demon_armor", "Demon Armor", "armor", "Armor, Shadow resistance and health regeneration increased.",
       "Buff", "Shadow", dispel="Magic", duration=1800, exclusiveGroup="warlock_armor", tags=["Armor"],
       mods=[mod("Armor", 210, values=[210, 300, 390, 480, 570]),
             mod("Resistance", 3, school="Shadow", values=[3, 6, 9, 12, 15]),
             mod("HealthRegen", 7, values=[7, 12, 17, 22, 27])],
       _note="HealthRegen is per 5 sec (WoW: +3/5/7/9/11 health per 2 sec regen tick)."),
    AU("warlock_shadow_ward", "Shadow Ward", "shield", "Absorbing Shadow damage.", "Buff", "Shadow", dispel="Magic",
       duration=30, tags=["Ward"], absorb={"amount": 290, "perLevel": per_level(290, 875, SW_R), "schools": ["Shadow"]}),
    AU("warlock_banish", "Banish", "void", "Banished: cannot act, cannot be harmed.", "Debuff", "Shadow",
       dispel="Magic", duration=30, tags=["CC"], states=["Banish"], special="WarlockOneTargetCC"),
    AU("warlock_enslave_demon", "Enslave Demon", "demon", "Enslaved by a warlock.", "Debuff", "Shadow",
       dispel="Magic", duration=300, tags=["CC"],
       mods=[mod("MeleeHaste", -30, True), mod("CastSpeed", -20, True)], special="WarlockEnslaveDemon"),
    AU("warlock_inferno_stun", "Inferno", "meteor", "Stunned.", "Debuff", "Fire", duration=2, states=["Stun"]),
    AU("warlock_siphon_life", "Siphon Life", "drain", "Health drained to the warlock every 3 sec.", "Debuff",
       "Shadow", dispel="Magic", duration=30, tickInterval=3,
       tickEffects=[dmg(15, None, 45, None, SL_R, 0.1, pctOfDamage=100)]),
    AU("warlock_amplify_curse", "Amplify Curse", "curse", "Your next Curse of Weakness or Agony is 50% stronger "
       "(Curse of Exhaustion +20%).", "Buff", "Shadow", duration=30, charges=1, special="WarlockAmplifyCurse"),
    AU("warlock_shadow_trance", "Shadow Trance", "shadow", "Your next Shadow Bolt is instant.", "Buff", "Shadow",
       duration=10, charges=1, special="WarlockShadowTrance"),
    AU("warlock_improved_drain_soul", "Improved Drain Soul", "soul_shard",
       "Mana regeneration increased by 100%; 50% continues while casting.", "Buff", "Shadow", duration=10,
       mods=[mod("SpiritRegenWhileCasting", 50)], special="WarlockImprovedDrainSoul"),
    AU("warlock_fel_domination", "Fel Domination", "demon",
       "Next demon summon: casting time -5.5 sec, mana cost -50%.", "Buff", "Shadow", duration=15, charges=1,
       special="WarlockFelDomination"),
    AU("warlock_shadowburn", "Shadowburn", "shadow", "If this unit dies, the warlock gains a Soul Shard.", "Debuff",
       "Shadow", duration=5, hidden=True, special="WarlockShadowburnShard"),
    AU("warlock_aftermath", "Aftermath", "fire", "Dazed: movement speed reduced by 50%.", "Debuff", "Fire",
       duration=5, states=["Daze"], mods=[mod("MoveSpeed", -50, True)]),
    AU("warlock_pyroclasm", "Pyroclasm", "fire", "Stunned.", "Debuff", "Fire", duration=3, states=["Stun"]),
    AU("warlock_soul_link", "Soul Link", "demon", "30% of damage taken is redirected to your demon; damage +3%.",
       "Buff", "Shadow", duration=0, mods=[mod("DamageDone", 3, True)], special="WarlockSoulLink"),
    AU("warlock_soul_link_pet", "Soul Link", "demon", "Damage increased by 3% while linked to the master.", "Buff",
       "Shadow", duration=0, mods=[mod("DamageDone", 3, True)]),
    AU("warlock_demonic_sacrifice_imp", "Burning Wish", "fire", "Fire damage increased by 15%.", "Buff", "Fire",
       dispel="Magic", duration=1800, exclusiveGroup="warlock_demonic_sacrifice", tags=["DemonicSacrifice"],
       mods=[mod("DamageDone", 15, True, "Fire")]),
    AU("warlock_demonic_sacrifice_voidwalker", "Fel Stamina", "heart", "Restores 3% of total health every 4 sec.",
       "Buff", "Shadow", dispel="Magic", duration=1800, exclusiveGroup="warlock_demonic_sacrifice",
       tags=["DemonicSacrifice"], tickInterval=4, tickEffects=[{"type": "Heal", "pctOfMax": 3}]),
    AU("warlock_demonic_sacrifice_succubus", "Touch of Shadow", "shadow", "Shadow damage increased by 15%.", "Buff",
       "Shadow", dispel="Magic", duration=1800, exclusiveGroup="warlock_demonic_sacrifice",
       tags=["DemonicSacrifice"], mods=[mod("DamageDone", 15, True, "Shadow")]),
    AU("warlock_demonic_sacrifice_felhunter", "Fel Energy", "potion_blue", "Restores 2% of total mana every 4 sec.",
       "Buff", "Shadow", dispel="Magic", duration=1800, exclusiveGroup="warlock_demonic_sacrifice",
       tags=["DemonicSacrifice"], tickInterval=4,
       tickEffects=[{"type": "GainResource", "resource": "Mana", "pctOfMax": 2}]),
    AU("warlock_master_demonologist_imp", "Master Demonologist", "imp", "Threat caused reduced.", "Buff", "Shadow",
       duration=0, exclusiveGroup="warlock_master_demonologist", mods=[mod("ThreatGenerated", -4, True)],
       _note="Rank-1 values; WarlockMasterDemonologist multiplies them by the talent rank."),
    AU("warlock_master_demonologist_voidwalker", "Master Demonologist", "void", "Physical damage taken reduced.",
       "Buff", "Shadow", duration=0, exclusiveGroup="warlock_master_demonologist",
       mods=[mod("DamageTaken", -2, True, "Physical")]),
    AU("warlock_master_demonologist_succubus", "Master Demonologist", "demon", "All damage increased.", "Buff",
       "Shadow", duration=0, exclusiveGroup="warlock_master_demonologist", mods=[mod("DamageDone", 2, True)]),
    AU("warlock_master_demonologist_felhunter", "Master Demonologist", "eye", "All resistances increased.", "Buff",
       "Shadow", duration=0, exclusiveGroup="warlock_master_demonologist", mods=[mod("Resistance", 0.2)],
       _note="0.2 resistance per caster level per rank (handler sets value = 0.2 x level x rank)."),
    # demon auras
    AU("warlock_imp_blood_pact", "Blood Pact", "blood", "Party members within 30 yards gain Stamina.", "Buff",
       "Shadow", duration=0, tags=["Aura"], radius=30, radiusAura="warlock_imp_blood_pact_buff",
       radiusAffects="Allies"),
    AU("warlock_imp_blood_pact_buff", "Blood Pact", "blood", "Stamina increased.", "Buff", "Shadow", duration=0,
       mods=[mod("Stamina", 3, perLevel=per_level(3, 42, [4, 50]))],
       _note="3 at level 4 up to 42 at level 50 (perLevel uses the Imp's level via warlock_imp_blood_pact)."),
    AU("warlock_imp_fire_shield", "Fire Shield", "shield", "Melee attackers take Fire damage.", "Buff", "Fire",
       dispel="Magic", duration=180,
       procs=[{"trigger": "OnStruck", "chance": 100,
               "effects": [{"type": "Damage", "target": "Attacker", "school": "Fire", "min": 3,
                            "perLevel": per_level(3, 20, [14, 54])}]}]),
    AU("warlock_imp_phase_shift", "Phase Shift", "void", "Phased out: cannot be attacked, cannot act.", "Buff",
       "Shadow", duration=0, states=["Untargetable", "Invulnerable"], breakOnAction=True),
    AU("warlock_voidwalker_sacrifice", "Sacrifice", "shield", "Absorbing damage.", "Buff", "Shadow", dispel="Magic",
       duration=30, absorb={"amount": 305, "perLevel": per_level(305, 1905, [16, 56])}),
    AU("warlock_succubus_seduction", "Seduction", "heart", "Seduced: cannot act.", "Debuff", "Shadow",
       dispel="Magic", duration=15, tags=["CC"], states=["Incapacitate"], breakOnDamage=True),
    AU("warlock_succubus_lesser_invisibility", "Lesser Invisibility", "stealth", "Invisible.", "Buff", "Shadow",
       duration=300, states=["Invisible"], breakOnAction=True),
    AU("warlock_felhunter_spell_lock", "Spell Lock", "lock", "Silenced.", "Debuff", "Shadow", dispel="Magic",
       duration=4, states=["Silence"]),
    AU("warlock_felhunter_tainted_blood", "Tainted Blood", "blood", "Attackers take Shadow damage.", "Buff",
       "Shadow", duration=10,
       procs=[{"trigger": "OnStruck", "chance": 100,
               "effects": [{"type": "Damage", "target": "Attacker", "school": "Shadow", "min": 8,
                            "perLevel": per_level(8, 26, [32, 56])}]}]),
    AU("warlock_felhunter_paranoia", "Paranoia", "eye", "Party stealth detection increased.", "Buff", "Shadow",
       duration=0, tags=["Aura"], radius=30, radiusAura="warlock_felhunter_paranoia_buff", radiusAffects="Allies"),
    AU("warlock_felhunter_paranoia_buff", "Paranoia", "eye", "Stealth detection increased.", "Buff", "Shadow",
       duration=0, mods=[mod("StealthDetection", 5)]),
    AU("warlock_infernal_immolation", "Immolation", "fire", "Burns nearby enemies every 2 sec.", "Buff", "Fire",
       duration=0, tickInterval=2,
       tickEffects=[{"type": "Damage", "target": "EnemiesInRadius", "radius": 8, "school": "Fire", "min": 40}]),
]

for i in range(1, 6):
    auras.append(AU("warlock_shadow_vulnerability_%d" % i, "Shadow Vulnerability", "shadow",
                    "Shadow damage taken increased by %d%%. Consumed by 4 Shadow damage hits." % (4 * i), "Debuff",
                    "Shadow", duration=12, charges=4, exclusiveGroup="warlock_shadow_vulnerability",
                    mods=[mod("DamageTaken", 4 * i, True, "Shadow")],
                    procs=[{"trigger": "OnDamaged", "schools": ["Shadow"], "consumeCharge": True, "effects": []}],
                    _note="WoW counts only non-periodic damage; the proc cannot exclude periodic ticks."))
for t, name, lvl, hp, mp, cost in SOULSTONES:
    auras.append(AU("warlock_soulstone_%s" % t, "Soulstone Resurrection", "soul_shard",
                    "Soul stored: if downed, may rise with %d health and %d mana." % (hp, mp), "Buff", "Shadow",
                    duration=1800, exclusiveGroup="warlock_soulstone", persistThroughDeath=True,
                    special="WarlockSoulstoneResurrection"))
for t, name, lvl, absorb_amt, cost in SPELLSTONES:
    auras.append(AU("warlock_spellstone_%s" % t, "Spellstone", "sparkle", "Absorbing magical damage.", "Buff",
                    "Shadow", duration=60,
                    absorb={"amount": absorb_amt,
                            "schools": ["Holy", "Fire", "Nature", "Frost", "Shadow", "Arcane"]}))

# ---------------------------------------------------------------------------------------------- talents
DESTRO_TAG = ["Destruction"]
nightfall_procs = [
    proc("OnPeriodicDamage", [2, 4], [apply("warlock_shadow_trance", target="Self")], abilities=["warlock_corruption"]),
    proc("OnSpellHit", [2, 4], [apply("warlock_shadow_trance", target="Self")], abilities=["warlock_drain_life"]),
]
isb_procs = []
for i in range(5):
    vals = [0] * 5
    vals[i] = 100
    isb_procs.append(proc("OnSpellCrit", vals, [apply("warlock_shadow_vulnerability_%d" % (i + 1))],
                          abilities=["warlock_shadow_bolt"]))

affliction = [
    T("warlock_affliction_suppression", "Suppression", "eye", 1, 1, 5,
      "Reduces the chance for enemies to resist your Affliction spells by {2/4/6/8/10}%.",
      [amod("HitChance", 2, tags=["Affliction"])]),
    T("warlock_affliction_improved_corruption", "Improved Corruption", "skull", 1, 2, 5,
      "Reduces the casting time of your Corruption spell by {0.4/0.8/1.2/1.6/2} sec.",
      [amod("CastTime", -0.4, abilities=["warlock_corruption"])]),
    T("warlock_affliction_improved_curse_of_weakness", "Improved Curse of Weakness", "curse", 2, 0, 3,
      "Increases the effect of your Curse of Weakness by {6/13/20}%.",
      [amod("Effect", values=[6, 13, 20], abilities=["warlock_curse_of_weakness"])]),
    T("warlock_affliction_improved_drain_soul", "Improved Drain Soul", "soul_shard", 2, 1, 2,
      "Gives you a {50/100}% chance to get a 100% increase to your Mana regeneration for 10 sec if the target is "
      "killed by you while you drain its soul. In addition your Mana may continue to regenerate while casting at "
      "50% of normal.",
      [proc("OnKill", [50, 100], [apply("warlock_improved_drain_soul", target="Self")],
            abilities=["warlock_drain_soul"])]),
    T("warlock_affliction_improved_life_tap", "Improved Life Tap", "blood", 2, 2, 2,
      "Increases the amount of Mana awarded by your Life Tap spell by {10/20}%.",
      [amod("Effect", 10, abilities=["warlock_life_tap"])]),
    T("warlock_affliction_improved_drain_life", "Improved Drain Life", "drain", 2, 3, 5,
      "Increases the Health drained by your Drain Life spell by {2/4/6/8/10}%.",
      [amod("Damage", 2, abilities=["warlock_drain_life"])]),
    T("warlock_affliction_improved_curse_of_agony", "Improved Curse of Agony", "curse", 3, 0, 3,
      "Increases the damage done by your Curse of Agony by {2/4/6}%.",
      [amod("Effect", 2, abilities=["warlock_curse_of_agony"])]),
    T("warlock_affliction_fel_concentration", "Fel Concentration", "drain", 3, 1, 5,
      "Gives you a {14/28/42/56/70}% chance to avoid interruption caused by damage while channeling the Drain Life, "
      "Drain Mana, or Drain Soul spell.", [special("WarlockPushbackResist", [14, 28, 42, 56, 70])]),
    T("warlock_affliction_amplify_curse", "Amplify Curse", "curse", 3, 2, 1,
      "Increases the effect of your next Curse of Weakness or Curse of Agony by 50%, or your next Curse of "
      "Exhaustion by 20%. Lasts 30 sec. 3 min cooldown.", [grant("warlock_amplify_curse")]),
    T("warlock_affliction_grim_reach", "Grim Reach", "skull", 4, 0, 2,
      "Increases the range of your Affliction spells by {10/20}%.", [amod("Range", 10, tags=["Affliction"])]),
    T("warlock_affliction_nightfall", "Nightfall", "moon", 4, 1, 2,
      "Gives your Corruption and Drain Life spells a {2/4}% chance to cause you to enter a Shadow Trance state after "
      "damaging the opponent. The Shadow Trance state reduces the casting time of your next Shadow Bolt spell by "
      "100%.", nightfall_procs),
    T("warlock_affliction_improved_drain_mana", "Improved Drain Mana", "drain", 4, 3, 2,
      "Causes {15/30}% of the mana drained by your Drain Mana spell to damage the opponent.",
      [special("WarlockImprovedDrainMana", [15, 30])]),
    T("warlock_affliction_siphon_life", "Siphon Life", "drain", 5, 1, 1,
      "Transfers 15 health from the target to the caster every 3 sec. Lasts 30 sec.",
      [grant("warlock_siphon_life")]),
    T("warlock_affliction_curse_of_exhaustion", "Curse of Exhaustion", "curse", 5, 2, 1,
      "Reduces the target's movement speed by 10% for 12 sec. Only one Curse per Warlock can be active on any one "
      "target.", [grant("warlock_curse_of_exhaustion")], requires="warlock_affliction_amplify_curse"),
    T("warlock_affliction_improved_curse_of_exhaustion", "Improved Curse of Exhaustion", "curse", 5, 3, 4,
      "Increases the speed reduction of your Curse of Exhaustion by {5/10/15/20}%. Requires Curse of Exhaustion.",
      [amod("Effect", values=[50, 100, 150, 200], abilities=["warlock_curse_of_exhaustion"])],
      note="WoW prerequisite is Curse of Exhaustion via a same-tier (sideways) arrow, which the validator rejects "
           "('prerequisite must be on an earlier tier'); the requirement is stated in the description only. "
           "Effect +50% per rank turns the 10% slow into 15/20/25/30%."),
    T("warlock_affliction_shadow_mastery", "Shadow Mastery", "shadow", 6, 1, 5,
      "Increases the damage dealt or life drained by your Shadow spells by {2/4/6/8/10}%.",
      [stat("DamageDone", 2, pct=True, school="Shadow")], requires="warlock_affliction_siphon_life"),
    T("warlock_affliction_dark_pact", "Dark Pact", "moon", 7, 1, 1,
      "Drains 150 of your pet's Mana, returning 100% to you.", [grant("warlock_dark_pact")]),
]

demonology = [
    T("warlock_demonology_improved_healthstone", "Improved Healthstone", "heart", 1, 0, 2,
      "Increases the amount of Health restored by your Healthstone by {10/20}%.",
      [amod("Healing", 10, abilities=HS_USES)]),
    T("warlock_demonology_improved_imp", "Improved Imp", "imp", 1, 1, 3,
      "Increases the effect of your Imp's Firebolt, Fire Shield, and Blood Pact spells by {10/20/30}%.",
      [amod("Damage", 10, abilities=["warlock_imp_firebolt"]),
       amod("Effect", 10, abilities=["warlock_imp_fire_shield", "warlock_imp_blood_pact"])]),
    T("warlock_demonology_demonic_embrace", "Demonic Embrace", "demon", 1, 2, 5,
      "Increases your total Stamina by {3/6/9/12/15}% but reduces your total Spirit by {1/2/3/4/5}%.",
      [stat("Stamina", 3, pct=True), stat("Spirit", -1, pct=True)]),
    T("warlock_demonology_improved_health_funnel", "Improved Health Funnel", "heart", 2, 0, 2,
      "Increases the amount of Health transferred by your Health Funnel spell by {10/20}%.",
      [amod("Healing", 10, abilities=["warlock_health_funnel"])]),
    T("warlock_demonology_improved_voidwalker", "Improved Voidwalker", "void", 2, 1, 3,
      "Increases the effectiveness of your Voidwalker's Torment, Consume Shadows, Sacrifice and Suffering spells by "
      "{10/20/30}%.",
      [amod("Effect", 10, abilities=["warlock_voidwalker_torment", "warlock_voidwalker_sacrifice",
                                     "warlock_voidwalker_suffering"]),
       amod("Healing", 10, abilities=["warlock_voidwalker_consume_shadows"])]),
    T("warlock_demonology_fel_intellect", "Fel Intellect", "brain", 2, 2, 5,
      "Increases the maximum Mana of your Imp, Voidwalker, Succubus, and Felhunter by {3/6/9/12/15}%.",
      [stat("Mana", 3, pct=True, target="Pet")]),
    T("warlock_demonology_improved_succubus", "Improved Succubus", "demon", 3, 0, 3,
      "Increases the effect of your Succubus' Lash of Pain and Soothing Kiss spells by {10/20/30}%, and increases "
      "the duration of your Succubus' Seduction and Lesser Invisibility spells by {10/20/30}%.",
      [amod("Damage", 10, abilities=["warlock_succubus_lash_of_pain"]),
       amod("Effect", 10, abilities=["warlock_succubus_soothing_kiss"]),
       amod("DurationPct", 10, abilities=["warlock_succubus_seduction", "warlock_succubus_lesser_invisibility"])]),
    T("warlock_demonology_fel_domination", "Fel Domination", "demon", 3, 1, 1,
      "Your next Imp, Voidwalker, Succubus, or Felhunter Summon spell has its casting time reduced by 5.5 sec and "
      "its Mana cost reduced by 50%. 15 min cooldown.", [grant("warlock_fel_domination")]),
    T("warlock_demonology_fel_stamina", "Fel Stamina", "heart", 3, 2, 5,
      "Increases the maximum Health of your Imp, Voidwalker, Succubus, and Felhunter by {3/6/9/12/15}%.",
      [stat("Health", 3, pct=True, target="Pet")]),
    T("warlock_demonology_master_summoner", "Master Summoner", "demon", 4, 1, 2,
      "Reduces the casting time of your Imp, Voidwalker, Succubus, and Felhunter Summoning spells by {2/4} sec and "
      "the Mana cost by {20/40}%.",
      [amod("CastTime", -2, abilities=SUMMONS), amod("Cost", -20, abilities=SUMMONS)],
      requires="warlock_demonology_fel_domination"),
    T("warlock_demonology_unholy_power", "Unholy Power", "demon", 4, 2, 5,
      "Increases the damage done by your Voidwalker, Succubus, and Felhunter's melee attacks and your Imp's Firebolt "
      "by {4/8/12/16/20}%.", [stat("DamageDone", 4, pct=True, target="Pet")],
      note="Applied as pet DamageDone (also touches Lash of Pain, a slight over-application)."),
    T("warlock_demonology_improved_enslave_demon", "Improved Enslave Demon", "demon", 5, 0, 5,
      "Reduces the Attack Speed and Casting Speed penalty of your Enslave Demon spell by {2/4/6/8/10}% and reduces "
      "the resist chance by {2/4/6/8/10}%.",
      [amod("HitChance", 2, abilities=["warlock_enslave_demon"]),
       amod("Effect", values=[-6.7, -13.3, -20, -26.7, -33.3], abilities=["warlock_enslave_demon"])],
      note="Effect -6.7% per rank shrinks the 30%/20% penalties by about 2 points each."),
    T("warlock_demonology_demonic_sacrifice", "Demonic Sacrifice", "skull", 5, 1, 1,
      "When activated, sacrifices your summoned demon to grant you an effect that lasts 30 min. The effect is "
      "canceled if any Demon is summoned.", [grant("warlock_demonic_sacrifice")]),
    T("warlock_demonology_improved_firestone", "Improved Firestone", "ember", 5, 3, 2,
      "Increases the bonus Fire damage from Firestones and the Firestone effect by {15/30}%.",
      [amod("Damage", 15, abilities=FIRE_FLAMES)]),
    T("warlock_demonology_master_demonologist", "Master Demonologist", "demon", 6, 2, 5,
      "Grants both the Warlock and the summoned demon an effect as long as that demon is active. Imp - Reduces "
      "threat caused by {4/8/12/16/20}%. Voidwalker - Reduces physical damage taken by {2/4/6/8/10}%. Succubus - "
      "Increases all damage caused by {2/4/6/8/10}%. Felhunter - Increases all resistances by "
      "{0.2/0.4/0.6/0.8/1} per level.",
      [special("WarlockMasterDemonologist", [1, 2, 3, 4, 5])], requires="warlock_demonology_unholy_power"),
    T("warlock_demonology_soul_link", "Soul Link", "demon", 7, 1, 1,
      "When active, 30% of all damage taken by the caster is taken by your Imp, Voidwalker, Succubus, or Felhunter "
      "demon instead. In addition, both the demon and master will inflict 3% more damage. Lasts as long as the demon "
      "is active.", [grant("warlock_soul_link")], requires="warlock_demonology_demonic_sacrifice"),
    T("warlock_demonology_improved_spellstone", "Improved Spellstone", "sparkle", 7, 2, 2,
      "Increases the amount of damage absorbed by your Spellstone by {15/30}%.",
      [amod("Effect", 15, abilities=SPS_USES)]),
]

destruction = [
    T("warlock_destruction_improved_shadow_bolt", "Improved Shadow Bolt", "shadow", 1, 1, 5,
      "Your Shadow Bolt critical strikes increase Shadow damage dealt to the target by {4/8/12/16/20}% until 4 "
      "non-periodic damage sources are applied. Effect lasts a maximum of 12 sec.", isb_procs,
      note="One proc per rank (one-hot chances) so each rank applies its own vulnerability aura."),
    T("warlock_destruction_cataclysm", "Cataclysm", "fire", 1, 2, 5,
      "Reduces the Mana cost of your Destruction spells by {1/2/3/4/5}%.", [amod("Cost", -1, tags=DESTRO_TAG)]),
    T("warlock_destruction_bane", "Bane", "shadow", 2, 1, 5,
      "Reduces the casting time of your Shadow Bolt and Immolate spells by {0.1/0.2/0.3/0.4/0.5} sec and your Soul "
      "Fire spell by {0.4/0.8/1.2/1.6/2} sec.",
      [amod("CastTime", -0.1, abilities=["warlock_shadow_bolt", "warlock_immolate"]),
       amod("CastTime", -0.4, abilities=["warlock_soul_fire"])]),
    T("warlock_destruction_aftermath", "Aftermath", "fire", 2, 2, 5,
      "Gives your Destruction spells a {2/4/6/8/10}% chance to daze the target for 5 sec.",
      [proc("OnSpellHit", [2, 4, 6, 8, 10], [apply("warlock_aftermath")], tags=DESTRO_TAG)]),
    T("warlock_destruction_improved_firebolt", "Improved Firebolt", "fireball", 3, 0, 2,
      "Reduces the casting time of your Imp's Firebolt spell by {0.5/1} sec.",
      [amod("CastTime", -0.5, abilities=["warlock_imp_firebolt"])]),
    T("warlock_destruction_improved_lash_of_pain", "Improved Lash of Pain", "tentacle", 3, 1, 2,
      "Reduces the cooldown of your Succubus' Lash of Pain spell by {3/6} sec.",
      [amod("Cooldown", -3, abilities=["warlock_succubus_lash_of_pain"])]),
    T("warlock_destruction_devastation", "Devastation", "fire", 3, 2, 5,
      "Increases the critical strike chance of your Destruction spells by {1/2/3/4/5}%.",
      [amod("CritChance", 1, tags=DESTRO_TAG)]),
    T("warlock_destruction_shadowburn", "Shadowburn", "shadow", 3, 3, 1,
      "Instantly blasts the target for 91 to 104 Shadow damage. If the target dies within 5 sec of Shadowburn, and "
      "yields experience or honor, the caster gains a Soul Shard.", [grant("warlock_shadowburn")]),
    T("warlock_destruction_intensity", "Intensity", "fire", 4, 0, 2,
      "Gives you a {35/70}% chance to resist interruption caused by damage while casting or channeling any "
      "Destruction spell.", [special("WarlockPushbackResist", [35, 70])]),
    T("warlock_destruction_destructive_reach", "Destructive Reach", "fire", 4, 1, 2,
      "Increases the range of your Destruction spells by {10/20}%.", [amod("Range", 10, tags=DESTRO_TAG)]),
    T("warlock_destruction_improved_searing_pain", "Improved Searing Pain", "ember", 4, 3, 5,
      "Increases the critical strike chance of your Searing Pain spell by {2/4/6/8/10}%.",
      [amod("CritChance", 2, abilities=["warlock_searing_pain"])]),
    T("warlock_destruction_pyroclasm", "Pyroclasm", "meteor", 5, 0, 2,
      "Gives your Rain of Fire, Hellfire, and Soul Fire spells a {13/26}% chance to stun the target for 3 sec.",
      [proc("OnSpellHit", [13, 26], [apply("warlock_pyroclasm")],
            abilities=["warlock_rain_of_fire", "warlock_hellfire", "warlock_soul_fire"])],
      requires="warlock_destruction_intensity"),
    T("warlock_destruction_improved_immolate", "Improved Immolate", "fire", 5, 1, 5,
      "Increases the initial damage of your Immolate spell by {5/10/15/20/25}%.",
      [amod("Damage", 5, abilities=["warlock_immolate"])],
      note="Damage applies to direct Damage effects only; the DoT is the Effect property."),
    T("warlock_destruction_ruin", "Ruin", "skull", 5, 2, 1,
      "Increases the critical strike damage bonus of your Destruction spells by 100%.",
      [amod("CritBonus", 100, tags=DESTRO_TAG)], requires="warlock_destruction_devastation"),
    T("warlock_destruction_emberstorm", "Emberstorm", "fire", 6, 2, 5,
      "Increases the damage done by your Fire spells by {2/4/6/8/10}%.",
      [stat("DamageDone", 2, pct=True, school="Fire")]),
    T("warlock_destruction_conflagrate", "Conflagrate", "fire", 7, 1, 1,
      "Ignites a target that is already afflicted by your Immolate, dealing 249 to 316 Fire damage and consuming the "
      "Immolate spell.", [grant("warlock_conflagrate")], requires="warlock_destruction_improved_immolate"),
]

trees = [
    {"id": "tree_warlock_affliction", "classId": "Warlock", "name": "Affliction", "icon": "skull",
     "description": "Curses, drains and damage over time: Nightfall, Siphon Life, Shadow Mastery and Dark Pact.",
     "talents": affliction},
    {"id": "tree_warlock_demonology", "classId": "Warlock", "name": "Demonology", "icon": "demon",
     "description": "Stronger demons and stones: Fel Domination, Demonic Sacrifice, Master Demonologist and Soul Link.",
     "talents": demonology},
    {"id": "tree_warlock_destruction", "classId": "Warlock", "name": "Destruction", "icon": "fire",
     "description": "Raw Shadow and Fire: Improved Shadow Bolt, Ruin, Shadowburn and Conflagrate.",
     "talents": destruction},
]

default_build = (
    rep("warlock_affliction_improved_corruption", 5) + rep("warlock_affliction_improved_life_tap", 2) +
    rep("warlock_affliction_improved_drain_life", 3) + rep("warlock_affliction_improved_curse_of_agony", 3) +
    ["warlock_affliction_amplify_curse", "warlock_affliction_fel_concentration"] +
    rep("warlock_affliction_nightfall", 2) + rep("warlock_affliction_grim_reach", 2) +
    ["warlock_affliction_improved_drain_life", "warlock_affliction_siphon_life",
     "warlock_affliction_curse_of_exhaustion"] +
    rep("warlock_affliction_fel_concentration", 3) + rep("warlock_affliction_shadow_mastery", 5) +
    ["warlock_affliction_dark_pact"] +
    rep("warlock_demonology_demonic_embrace", 5) + rep("warlock_demonology_improved_voidwalker", 3) +
    rep("warlock_demonology_improved_healthstone", 2) + ["warlock_demonology_fel_domination"] +
    rep("warlock_demonology_fel_stamina", 4) + rep("warlock_demonology_master_summoner", 2) +
    rep("warlock_demonology_unholy_power", 3)
)
assert len(default_build) == 51, len(default_build)

# ---------------------------------------------------------------------------------------------- demons
creatures = [
    {"id": "warlock_demon_imp", "name": "Imp", "sprite": "demon_imp", "type": "Demon", "family": "Imp",
     "description": "A cackling little fire imp: fragile, mana-rich and endlessly sarcastic. Casts Firebolt from "
                    "range and keeps Blood Pact up on the party.",
     "rank": "Pet", "levelMin": 1, "levelMax": 60, "healthMult": 0.55, "damageMult": 0.3, "armorMult": 0.5,
     "manaMult": 1.5, "resource": "Mana", "attackSpeed": 2.0, "meleeSchool": "Physical", "size": 0.9, "ai": "Pet",
     "abilities": [
         {"ability": "warlock_imp_blood_pact", "priority": 9, "condition": "targetNoAura:warlock_imp_blood_pact"},
         {"ability": "warlock_imp_firebolt", "priority": 5},
         {"ability": "warlock_imp_fire_shield", "priority": 3, "chance": 30},
         {"ability": "warlock_imp_phase_shift", "priority": 0, "chance": 0}],
     "stats": [mod("Resistance", 20, school="Fire")]},
    {"id": "warlock_demon_voidwalker", "name": "Voidwalker", "sprite": "demon_voidwalker", "type": "Demon",
     "family": "Voidwalker",
     "description": "A hulking shade of the void that soaks blows for its master: the warlock's tank.",
     "rank": "Pet", "levelMin": 1, "levelMax": 60, "healthMult": 1.4, "damageMult": 0.6, "armorMult": 2.0,
     "manaMult": 0.8, "resource": "Mana", "attackSpeed": 2.0, "meleeSchool": "Physical", "size": 2.2, "ai": "Pet",
     "abilities": [
         {"ability": "warlock_voidwalker_torment", "priority": 7},
         {"ability": "warlock_voidwalker_suffering", "priority": 6, "condition": "enemiesInRange:2"},
         {"ability": "warlock_voidwalker_consume_shadows", "priority": 3, "condition": "selfHpBelow:40"},
         {"ability": "warlock_voidwalker_sacrifice", "priority": 1, "chance": 0}],
     "stats": [mod("Resistance", 10, school="Shadow")]},
    {"id": "warlock_demon_succubus", "name": "Succubus", "sprite": "demon_succubus", "type": "Demon",
     "family": "Succubus",
     "description": "A whip-wielding temptress: solid melee damage and Seduction, the warlock's best humanoid "
                    "crowd control.",
     "rank": "Pet", "levelMin": 1, "levelMax": 60, "healthMult": 1.0, "damageMult": 1.0, "armorMult": 1.0,
     "manaMult": 1.0, "resource": "Mana", "attackSpeed": 2.0, "meleeSchool": "Physical", "size": 1.9, "ai": "Pet",
     "abilities": [
         {"ability": "warlock_succubus_lash_of_pain", "priority": 5},
         {"ability": "warlock_succubus_soothing_kiss", "priority": 2, "chance": 25},
         {"ability": "warlock_succubus_seduction", "priority": 1, "chance": 0},
         {"ability": "warlock_succubus_lesser_invisibility", "priority": 0, "chance": 0}]},
    {"id": "warlock_demon_felhunter", "name": "Felhunter", "sprite": "demon_felhunter", "type": "Demon",
     "family": "Felhunter",
     "description": "A tentacled magic-eater: devours enemy buffs and silences casters with Spell Lock.",
     "rank": "Pet", "levelMin": 1, "levelMax": 60, "healthMult": 1.15, "damageMult": 0.8, "armorMult": 1.2,
     "manaMult": 1.0, "resource": "Mana", "attackSpeed": 2.0, "meleeSchool": "Physical", "size": 1.4, "ai": "Pet",
     "abilities": [
         {"ability": "warlock_felhunter_paranoia", "priority": 9, "condition": "targetNoAura:warlock_felhunter_paranoia"},
         {"ability": "warlock_felhunter_spell_lock", "priority": 9, "condition": "targetCasting"},
         {"ability": "warlock_felhunter_devour_magic", "priority": 5},
         {"ability": "warlock_felhunter_tainted_blood", "priority": 3, "condition": "selfHpBelow:60"}],
     "stats": [mod("Resistance", 15)]},
    {"id": "warlock_demon_infernal", "name": "Infernal", "sprite": "demon_infernal", "type": "Demon",
     "family": "Infernal",
     "description": "A meteor-born giant of fel stone wreathed in Immolation. Summoned by Inferno for 5 minutes.",
     "rank": "Pet", "levelMin": 50, "levelMax": 60, "healthMult": 2.0, "damageMult": 1.2, "armorMult": 1.5,
     "manaMult": 0, "resource": "None", "attackSpeed": 2.0, "meleeSchool": "Physical", "size": 2.6, "ai": "Pet",
     "passives": ["warlock_infernal_immolation"], "immune": ["Fear", "Polymorph", "Sleep"],
     "_note": "Art key demon_infernal is not in Docs/ArtKeys.md yet (placeholder fallback)."},
]

# ---------------------------------------------------------------------------------------------- class
warlock_class = {
    "id": "Warlock", "name": "Warlock",
    "description": "Students of forbidden shadow who bargain with demons. A warlock bleeds enemies with curses and "
                   "damage over time, fuels spells with their own life and fights beside a bound demon servant.",
    "roles": ["Ranged DPS", "Support"], "color": "#9482C9", "icon": "crest_warlock", "resource": "Mana", "gcd": 1.5,
    "armorTypes": ["Cloth"], "armorUpgrade": {"level": 40, "type": "None"},
    "weaponTypes": ["Staff", "OneHandSword", "Dagger", "Wand"], "dualWieldLevel": 0, "canParry": False,
    "canBlock": False,
    "baseStatsLevel1": {"strength": 20, "agility": 20, "stamina": 21, "intellect": 22, "spirit": 23},
    "baseStatsLevel60": {"strength": 40, "agility": 40, "stamina": 57, "intellect": 108, "spirit": 115},
    "baseHealthLevel1": 23, "baseHealthLevel60": 1414, "baseManaLevel1": 90, "baseManaLevel60": 1373,
    "meleeAp": "Strength", "rangedAp": "Strength",
    "agilityPerMeleeCritAt60": 20, "intellectPerSpellCritAt60": 60.6, "agilityPerDodgeAt60": 20,
    "baseMeleeCrit": 2.0, "baseSpellCrit": 1.7, "baseDodge": 2.0,
    "manaRegenBase": 15, "manaRegenPerSpirit": 0.2,
    "basicAttack": "attack",
    "startingAbilities": ["warlock_shadow_bolt", "warlock_demon_skin", "warlock_summon_imp", "shoot"],
    "startingItems": ["warlock_starter_dagger", "warlock_starter_robe", "warlock_starter_pants",
                      "warlock_starter_shoes"],
    "startingStance": "",
    "talentTrees": ["tree_warlock_affliction", "tree_warlock_demonology", "tree_warlock_destruction"],
    "defaultBuild": default_build,
    "sprite": "char_warlock", "portrait": "portrait_warlock",
    "designNotes": "The warlock wins slowly and surely: stack Corruption, Curse of Agony and Immolate, then Fear or "
                   "Drain while they tick. Life Tap turns health into mana, Drain Life turns it back, and a demon "
                   "fights at your side - the Voidwalker tanks, the Imp buffs Stamina, the Succubus seduces, the "
                   "Felhunter eats magic. Finish dying foes with Drain Soul to harvest the soul shards that summons, "
                   "stones and Shadowburn consume.",
    "_note": "Base stats/health/mana: naked Human Warlock (WoW Classic 1.12). Mana regen per 2 sec tick = 15 + "
             "Spirit/5. Spell crit: 1.7% base + 1% per 60.6 Intellect at 60.",
}

SPECIALS = [
    {"id": "WarlockConsumeSoulShard",
     "usedBy": "warlock_summon_voidwalker, warlock_summon_succubus, warlock_summon_felhunter, warlock_shadowburn, "
               "warlock_soul_fire, warlock_create_healthstone_*, warlock_create_soulstone_*, "
               "warlock_create_spellstone_*, warlock_create_firestone_*",
     "behaviour": "Ability-level requirement + reagent. The ability is unusable (greyed out, AI skips it) unless the "
                  "caster's party inventory holds at least 1 soul_shard. One soul_shard is removed when the ability "
                  "resolves (for cast-time abilities: when the pending cast completes, not when it starts; an "
                  "interrupted or cancelled cast consumes nothing). It is consumed even if the spell then misses. "
                  "All other effects of the ability are data-driven."},
    {"id": "WarlockDrainSoulShard", "usedBy": "warlock_drain_soul",
     "behaviour": "If the channel target dies (from any source) while the warlock is channelling Drain Soul on it "
                  "(including the turn-start resolution of remaining ticks) and the target would grant experience "
                  "(not grey for the warlock, not a Critter/Totem/summoned pet), add 1 soul_shard to the party "
                  "inventory (no more than 32 total). Also fires the warlock's OnKill procs with "
                  "ability = warlock_drain_soul (Improved Drain Soul)."},
    {"id": "WarlockShadowburnShard", "usedBy": "warlock_shadowburn (aura warlock_shadowburn)",
     "behaviour": "Hidden 5 sec debuff placed by Shadowburn. If the unit carrying it dies while it is active and "
                  "would grant experience, the aura's caster gains 1 soul_shard (max 32)."},
    {"id": "WarlockLifeTap", "usedBy": "warlock_life_tap",
     "behaviour": "Amount X = effect min + perLevel x (rankLevel - learnLevel) + coef (0.8) x the caster's Shadow "
                  "SpellDamage (20 at rank 1 ... 424 at rank 6). Requires current health > X (otherwise unusable). "
                  "The caster loses X health (not damage: no threat, cannot be absorbed, cannot be avoided) and gains "
                  "X x (1 + AbilityMod Effect% on warlock_life_tap, i.e. Improved Life Tap +10/20%) mana. Gaining "
                  "mana this way does not count as spending mana for the five-second rule."},
    {"id": "WarlockCurseOfAgonyRamp", "usedBy": "warlock_curse_of_agony (aura)",
     "behaviour": "The aura ticks 12 times (every 2 sec over 24 sec). Tick n (1-based) deals the computed tick "
                  "damage x 0.5 for n = 1-4, x 1.0 for n = 5-8 and x 1.5 for n = 9-12 (same total as 12 even ticks). "
                  "Refreshing the curse restarts the count."},
    {"id": "WarlockSearingPainThreat", "usedBy": "warlock_searing_pain",
     "behaviour": "Threat generated by Searing Pain's damage is doubled (2 threat per point of damage before other "
                  "threat modifiers such as ThreatGenerated)."},
    {"id": "WarlockOneTargetCC", "usedBy": "warlock_fear, warlock_banish (auras)",
     "behaviour": "Only one target per warlock: when the aura is applied by caster C, remove any other aura with the "
                  "same id that C applied to a different unit (e.g. a second Fear frees the first target)."},
    {"id": "WarlockEnslaveDemon", "usedBy": "warlock_enslave_demon (ability and aura)",
     "behaviour": "Ability: requires 1 soul_shard (consumed on success, as WarlockConsumeSoulShard) and a target of "
                  "type Demon whose level is <= 45 / 55 / 60 for rank 1 / 2 / 3 of the caster; otherwise the cast "
                  "fails. On hit the target leaves its encounter side, becomes the warlock's pet (dismissing the "
                  "current demon; one pet per owner) and receives the aura for 300 sec. Aura: while active the demon "
                  "has -30% MeleeHaste and -20% CastSpeed (scaled by AbilityMod Effect, Improved Enslave Demon). Each "
                  "time the demon's turn starts there is a 1% x (turns enslaved) chance it breaks free early. When "
                  "the aura ends for any reason the demon returns to the hostile team at full aggression against "
                  "the warlock."},
    {"id": "WarlockSoulstoneResurrection", "usedBy": "warlock_soulstone_minor ... warlock_soulstone_major (auras)",
     "behaviour": "Persists through death. When the holder becomes Downed (0 HP) or dies while the aura is active, "
                  "remove the aura and offer the holder a self-resurrection: at the start of their next turn they "
                  "stand up (no ally Help needed) with H health and M mana (capped at max), where by aura: minor "
                  "400/700, lesser 750/1200, normal 1100/1700, greater 1600/2200, major 2200/2800. The player may "
                  "decline (stay downed); the AI always accepts. Only one soulstone aura per unit "
                  "(exclusiveGroup warlock_soulstone)."},
    {"id": "WarlockAmplifyCurse", "usedBy": "warlock_amplify_curse (aura)",
     "behaviour": "While present (1 charge, 30 sec), the next warlock_curse_of_agony or warlock_curse_of_weakness "
                  "the holder applies has its magnitude x1.5 (CoA tick damage, CoW attack-power reduction), or the "
                  "next warlock_curse_of_exhaustion gets +20 percentage points of slow (10% -> 30%, before Improved "
                  "Curse of Exhaustion). Applying any of those three curses removes the aura; other curses do not."},
    {"id": "WarlockShadowTrance", "usedBy": "warlock_shadow_trance (aura)",
     "behaviour": "While present (1 charge, 10 sec), the holder's next warlock_shadow_bolt has castTime 0 (instant: "
                  "Time cost = GCD, resolves immediately). The aura is removed when that Shadow Bolt starts."},
    {"id": "WarlockFelDomination", "usedBy": "warlock_fel_domination (aura)",
     "behaviour": "While present (1 charge, 15 sec), the holder's next warlock_summon_imp/voidwalker/succubus/"
                  "felhunter has castTime reduced by 5.5 sec (after Master Summoner, minimum 0) and its mana cost "
                  "reduced by 50%. The aura is removed when that summon starts."},
    {"id": "WarlockDemonicSacrifice", "usedBy": "warlock_demonic_sacrifice",
     "behaviour": "Requires an active demon pet. Kill the pet (no loot/XP, no death events for the party) and apply "
                  "to the caster, by the pet's family: Imp -> warlock_demonic_sacrifice_imp, Voidwalker -> "
                  "warlock_demonic_sacrifice_voidwalker, Succubus -> warlock_demonic_sacrifice_succubus, Felhunter -> "
                  "warlock_demonic_sacrifice_felhunter. Other families (Infernal, enslaved demons) fail with no effect. "
                  "The auras are removed by any summon (summons carry RemoveAura auraTag DemonicSacrifice)."},
    {"id": "WarlockSoulLink", "usedBy": "warlock_soul_link (aura)",
     "behaviour": "While the warlock has the aura and its demon pet is alive: 30% of every damage instance the "
                  "warlock takes (after absorbs, before it is applied) is dealt to the pet instead (no threat, no "
                  "procs, cannot be avoided). If the pet dies or is dismissed/sacrificed, remove warlock_soul_link "
                  "from the warlock (and warlock_soul_link_pet from the pet)."},
    {"id": "WarlockMasterDemonologist", "usedBy": "warlock_demonology_master_demonologist (talent)",
     "behaviour": "While the warlock has a demon pet of family F, apply the matching aura to BOTH the warlock and the "
                  "pet with every mod value multiplied by the talent rank r (values[r-1]): Imp -> "
                  "warlock_master_demonologist_imp (ThreatGenerated -4% x r), Voidwalker -> "
                  "..._voidwalker (Physical DamageTaken -2% x r), Succubus -> ..._succubus (DamageDone +2% x r), "
                  "Felhunter -> ..._felhunter (Resistance to all schools +0.2 x r x warlock level). Remove/swap them "
                  "when the pet changes or dies."},
    {"id": "WarlockImprovedDrainMana", "usedBy": "warlock_affliction_improved_drain_mana (talent)",
     "behaviour": "Each Drain Mana tick additionally deals Shadow damage to the target equal to values[rank-1]% "
                  "(15/30) of the mana actually drained by that tick (no crit, no spell power, counts as the "
                  "warlock's damage)."},
    {"id": "WarlockImprovedDrainSoul", "usedBy": "warlock_improved_drain_soul (aura)",
     "behaviour": "While present, the holder's Spirit-based mana regeneration (manaRegenBase + Spirit x "
                  "manaRegenPerSpirit) is doubled; the aura's SpiritRegenWhileCasting +50 mod lets 50% of it "
                  "continue inside the five-second rule."},
    {"id": "WarlockPushbackResist",
     "usedBy": "warlock_affliction_fel_concentration, warlock_destruction_intensity (talents)",
     "behaviour": "Lanternvale has no casting pushback by default. If the engine implements damage pushback "
                  "(recommended: each damaging hit taken while a cast/channel is pending delays a cast by 0.5 sec "
                  "or removes 0.5 sec of remaining channel time, at most twice per pending cast), these talents give "
                  "values[rank-1]% chance to ignore each pushback: Fel Concentration for warlock_drain_life, "
                  "warlock_drain_mana and warlock_drain_soul (14..70%), Intensity for abilities tagged Destruction "
                  "(35/70%). Without pushback they have no effect."},
]

BUNDLE = {
    "classes": [warlock_class],
    "abilities": abilities,
    "auras": auras,
    "talentTrees": trees,
    "items": items,
    "creatures": creatures,
    "specials": SPECIALS,
}
