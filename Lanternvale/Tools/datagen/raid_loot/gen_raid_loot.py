#!/usr/bin/env python3
"""Raid loot generator (Docs/Expansion.md §8 "Raid loot design", builder **raidloot**).

Writes, under Assets/Lanternvale/Resources/Data/content/:
  raid_loot.json         the boss and trash loot tables lt_r1_* / lt_r2_*
  raid_loot_t1.json      tier 1 (Hollow Heart) class sets: 8 classes x 5 pieces, itemSets, set auras
  raid_loot_t2.json      tier 2 (Ashwyrm's Roost) class sets
  raid_loot_epics.json   the non-set raid epics of both raids, the 5 legendaries and their auras

The numbers come from the budget formulas (brief_items §3, Expansion.md §8 "Gear budget"):
  stats  = 0.55 x ilvl x Qa x SlotBudgetMult   (Qa: Epic 2.1, Legendary 2.8), spent with ItemGenerator.StatCost
  DPS    = ItemGenerator.WeaponDps x .86 (Epic) / .95 (Legendary); Off Hand weapons (off_hand=True): the one-hand
           DPS, the OffHand slot stat budget
  armour = ItemGenerator.ArmorValue; shields 30 x ilvl x 1.1 (x1.2 legendary), block 0.6 x ilvl + 3
  price  = 1.2 x ilvl^2.2 x slot x Q (Epic 15, Legendary 40), the wn_items formula
Names, flavour text, stat profiles and set bonuses are hand-curated below. The JSON is the source of truth once
committed; re-run this script (python3 gen_raid_loot.py) after changing the tables here, then Tools/check.sh core.
"""
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Lanternvale", "Resources", "Data", "content")

# ------------------------------------------------------------------------------------------------ budget formulas

QA = {"Epic": 2.1, "Legendary": 2.8}             # authored stat quality factor (Expansion.md §8)
DPS_RATIO = {"Epic": 0.86, "Legendary": 0.95}    # authored DPS / ItemGenerator.WeaponDps
Q_PRICE = {"Epic": 15, "Legendary": 40}
Q_INDEX = {"Rare": 3, "Epic": 4, "Legendary": 5}

SLOT_MULT = {  # ItemGenerator.SlotBudgetMult
    "Chest": 1.0, "Legs": 1.0, "Head": 1.0, "TwoHand": 1.0,
    "Shoulder": 0.75, "Hands": 0.75, "Feet": 0.75, "Waist": 0.75,
    "Wrist": 0.56, "Neck": 0.56, "Back": 0.56, "Finger": 0.56, "Trinket": 0.56, "OffHand": 0.56,
    "OneHand": 0.45, "MainHand": 0.45, "Ranged": 0.35,
}
STAT_COST = {  # ItemGenerator.StatCost
    "AttackPower": 0.5, "RangedAttackPower": 0.5, "SpellDamage": 0.86, "HealingPower": 0.45,
    "MeleeCrit": 14, "SpellCrit": 14, "RangedCrit": 14, "MeleeHit": 14, "SpellHit": 14, "Dodge": 14, "Parry": 14,
    "Armor": 0.1, "ManaRegen": 2.5, "HealthRegen": 2.5,
}
ARMOR_TYPE = {"Cloth": 1.4, "Leather": 2.8, "Mail": 5.9, "Plate": 10.0}
ARMOR_SLOT = {"Chest": 1.0, "Legs": 1.0, "Head": 0.81, "Shoulder": 0.75, "Feet": 0.69, "Hands": 0.62, "Waist": 0.56,
              "Wrist": 0.44, "Back": 0.5}
TWO_HAND = {"TwoHandAxe", "TwoHandMace", "TwoHandSword", "Polearm", "Staff"}


def stat_cost(stat):
    return STAT_COST.get(stat, 1.0)


def stat_budget(ilvl, q, slot):
    return 0.55 * ilvl * QA[q] * SLOT_MULT[slot]


def cs_round(x):
    """C# Math.Round (banker's rounding), same as Python's round on floats."""
    return int(round(x))


def armor_value(t, slot, ilvl, q):
    qm = 1.1 + 0.1 * (Q_INDEX[q] - Q_INDEX["Rare"])
    if slot == "Back":
        t = "Cloth"
    return cs_round(ARMOR_TYPE[t] * ARMOR_SLOT[slot] * (ilvl + 5) * qm)


def weapon_dps(w, ilvl, q):
    qm = 1.1 + 0.12 * (Q_INDEX[q] - Q_INDEX["Rare"])
    dps = (0.62 * ilvl + 2) * qm
    if w in TWO_HAND:
        dps *= 1.3
    elif w in ("Bow", "Gun", "Crossbow", "Thrown"):
        dps *= 0.95
    elif w == "Wand":
        dps *= 1.25
    return dps


def price(ilvl, slot, q):
    return int(round(1.2 * ilvl ** 2.2 * max(0.6, SLOT_MULT[slot]) * Q_PRICE[q], -1))


def spend(budget, profile, fixed=()):
    """Spend a stat budget: fixed (stat, value) pairs first, the rest by the profile's weights (share of budget)."""
    stats = []
    rest = budget
    for stat, v in fixed:
        stats.append({"stat": stat, "value": v})
        rest -= v * stat_cost(stat)
    total = sum(w for _, w in profile)
    for stat, w in profile:
        v = max(1, cs_round(rest * w / total / stat_cost(stat)))
        stats.append({"stat": stat, "value": v})
    return stats


def cost_of(stats):
    return sum(s["value"] * stat_cost(s["stat"]) for s in stats)


# ------------------------------------------------------------------------------------------------ stat profiles

PROFILES = {
    # tier sets (by class role; Warrior/Paladin lean tank, Priest/Shaman lean healer: the companions' roles)
    "warrior": [("Stamina", .42), ("Strength", .28), ("Defense", .18), ("Agility", .12)],
    "paladin": [("Stamina", .38), ("Strength", .26), ("Intellect", .18), ("Defense", .18)],
    "priest": [("Intellect", .30), ("Spirit", .22), ("Stamina", .16), ("HealingPower", .32)],
    "shaman": [("Intellect", .30), ("Stamina", .18), ("HealingPower", .32), ("ManaRegen", .20)],
    "hunter": [("Agility", .46), ("Stamina", .22), ("Intellect", .12), ("RangedAttackPower", .20)],
    "rogue": [("Agility", .44), ("Stamina", .22), ("Strength", .12), ("AttackPower", .22)],
    "mage": [("Intellect", .32), ("Stamina", .18), ("Spirit", .10), ("SpellDamage", .40)],
    "warlock": [("Stamina", .26), ("Intellect", .26), ("Spirit", .08), ("SpellDamage", .40)],
    # non-set gear
    "tank": [("Stamina", .45), ("Strength", .25), ("Defense", .30)],
    "caster": [("Intellect", .32), ("Stamina", .20), ("SpellDamage", .48)],
    "healer": [("Intellect", .30), ("Spirit", .25), ("Stamina", .12), ("HealingPower", .33)],
    "agi_melee": [("Agility", .50), ("Stamina", .25), ("AttackPower", .25)],
    "str_melee": [("Strength", .50), ("Stamina", .25), ("Agility", .25)],
    "hybrid_melee": [("Strength", .35), ("Agility", .35), ("Stamina", .30)],
    "ranged": [("Agility", .50), ("Stamina", .25), ("RangedAttackPower", .25)],
    "phys": [("Agility", .35), ("Strength", .30), ("Stamina", .35)],
}

# ------------------------------------------------------------------------------------------------ tier sets

SLOTS = ["Head", "Shoulder", "Chest", "Hands", "Legs"]
SLOT_KEY = {"Head": "head", "Shoulder": "shoulders", "Chest": "chest", "Hands": "hands", "Legs": "legs"}
SLOT_ICON = {"Shoulder": "armor", "Chest": "armor", "Hands": "hand", "Legs": "armor"}
ARMOR_OF = {"Warrior": "Mail", "Paladin": "Mail", "Hunter": "Leather", "Shaman": "Leather", "Rogue": "Leather",
            "Mage": "Cloth", "Priest": "Cloth", "Warlock": "Cloth"}

TIERS = {
    1: {"prefix": "r1", "req": 20, "ilvl": {"Hands": 26, "Shoulder": 27, "Legs": 27, "Head": 28, "Chest": 28}},
    2: {"prefix": "r2", "req": 30, "ilvl": {"Hands": 36, "Shoulder": 37, "Legs": 37, "Head": 38, "Chest": 38}},
}

# tier 2 head/chest carry a 1% secondary stat for the damage dealers
T2_EXTRA = {"Rogue": ("MeleeCrit", 1), "Hunter": ("RangedCrit", 1), "Mage": ("SpellCrit", 1), "Warlock": ("SpellHit", 1)}


def aura(aid, name, icon, desc, duration, mods=None, kind="Buff", **extra):
    a = {"id": aid, "name": name, "icon": icon, "description": desc, "kind": kind, "duration": duration}
    a.update(extra)
    if mods:
        a["mods"] = [dict(stat=s, value=v, **({"school": sc} if sc else {})) for s, v, sc in
                     [(m + (None,))[:3] for m in mods]]
    return a


def proc(trigger, effects, chance=None, ppm=None, icd=0, **filters):
    p = {"trigger": trigger}
    if ppm is not None:
        p["ppm"] = ppm
    if chance is not None:
        p["chance"] = chance
    p.update(filters)
    p["effects"] = effects
    if icd:
        p["internalCooldown"] = icd
    return p


def amod(prop, value, abilities=None, tags=None, schools=None):
    m = {"type": "AbilityMod", "property": prop, "value": value}
    if abilities:
        m["abilities"] = abilities
    if tags:
        m["tags"] = tags
    if schools:
        m["schools"] = schools
    return m


SETS = {
    # ------------------------------------------------------------------ tier 1: the Hollow Heart
    (1, "Warrior"): {
        "name": "Rootwall Battlegear", "key": "rootwall",
        "pieces": [
            ("Rootwall Helm", "crown", "Its crest is a knot of living root. It tightens a little whenever something charges you."),
            ("Rootwall Pauldrons", None, "Mossy on top, iron underneath, like the best kind of grandmother."),
            ("Rootwall Hauberk", None, "Each ring was threaded through a root of the Hollow and came out stronger for it."),
            ("Rootwall Gauntlets", None, "The knuckles are grown over with bark. Punching trees is now a family matter."),
            ("Rootwall Legguards", None, "You stand your ground, and the ground, frankly, stands you."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "ThreatGenerated", "value": 8}],
             "description": "Threat generated increased by 8%."},
            {"pieces": 4, "equipEffects": [amod("Threat", 25, abilities=["warrior_sunder_armor", "warrior_revenge"])],
             "description": "Sunder Armor and Revenge generate 25% more threat."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnStruck", [
                {"type": "ApplyAura", "aura": "r1_aura_rootwall_barkskin", "target": "Self"}], chance=6, icd=30)}],
             "description": "When struck in melee, a 6% chance to grow Rootwall Barkskin: damage taken reduced by 10% and armour increased by 300 for 10 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r1_aura_rootwall_barkskin", "Rootwall Barkskin", "leaf",
                       "Damage taken reduced by 10%. Armour increased by 300.", 10,
                       [("DamageTaken", -10), ("Armor", 300)])],
    },
    (1, "Paladin"): {
        "name": "Lanternwarden's Vigil", "key": "lanternwarden",
        "pieces": [
            ("Lanternwarden Helm", "crown", "A tiny lantern burns inside the visor. It has never once been asked to go out."),
            ("Lanternwarden Spaulders", None, "Engraved with the names of every lamplighter who kept the vigil before you."),
            ("Lanternwarden Chestguard", None, "Warm as a hearthstone. Small creatures keep trying to nap against it."),
            ("Lanternwarden Gauntlets", None, "Steady enough to carry a candle through a gale, or a hammer through a gnoll."),
            ("Lanternwarden Legguards", None, "They clink softly in time with your heartbeat, like a lantern on a hook."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cooldown", -2, abilities=["paladin_judgement"])],
             "description": "The cooldown of Judgement is reduced by 2 sec."},
            {"pieces": 4, "equipEffects": [amod("Damage", 15, abilities=["paladin_judgement", "paladin_exorcism"]),
                                           amod("Threat", 30, abilities=["paladin_judgement", "paladin_exorcism"])],
             "description": "Judgement and Exorcism deal 15% more damage and generate 30% more threat."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnStruck", [
                {"type": "Heal", "pctOfMax": 6, "target": "Self"},
                {"type": "GainResource", "resource": "Mana", "pctOfMax": 4, "target": "Self"}], chance=6, icd=30)}],
             "description": "When struck in melee, a 6% chance to rekindle the vigil: heals you for 6% of your maximum health and restores 4% of your maximum mana. (30 sec cooldown.)"},
        ],
        "auras": [],
    },
    (1, "Priest"): {
        "name": "Vestments of the Quiet Wick", "key": "quiet_wick",
        "pieces": [
            ("Quiet Wick Circlet", "crown", "A ring of beeswax and silver. It smells faintly of a chapel on a winter evening."),
            ("Quiet Wick Mantle", None, "Stitched by the Weeping Twins' acolytes, who wept only a little into the hem."),
            ("Quiet Wick Robe", None, "Its candlelight is too soft to read by, and exactly bright enough to hope by."),
            ("Quiet Wick Gloves", None, "Wax-dipped fingertips. Pinching out a flame never hurts; mending a wound never does either."),
            ("Quiet Wick Leggings", None, "They hush every step, so the wounded can sleep while you work."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cost", -6, tags=["Heal"])],
             "description": "Your healing spells cost 6% less mana."},
            {"pieces": 4, "equipEffects": [amod("Effect", 15, abilities=["priest_power_word_shield"]),
                                           amod("Duration", 3, abilities=["priest_renew"])],
             "description": "Power Word: Shield absorbs 15% more damage, and Renew lasts 3 sec longer."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnHealCast", [
                {"type": "GainResource", "resource": "Mana", "pctOfMax": 5, "target": "Self"}], chance=10, icd=20)}],
             "description": "Your heals have a 10% chance to light a Second Wick, restoring 5% of your maximum mana. (20 sec cooldown.)"},
        ],
        "auras": [],
    },
    (1, "Shaman"): {
        "name": "Mossmender's Raiment", "key": "mossmender",
        "pieces": [
            ("Mossmender Headdress", "mask", "Feathers, fern and one very patient snail, who has asked to stay."),
            ("Mossmender Spaulders", None, "Living moss grows on them, and it grows back faster than anything can wound it."),
            ("Mossmender Tunic", None, "Soft leather tanned in spring water from beneath the Hollow's roots."),
            ("Mossmender Grips", None, "Damp, cool and smelling of rain. Fevers flee from them."),
            ("Mossmender Kilt", None, "Woven reeds over leather. It rustles like a pond full of frogs being polite."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cost", -6, tags=["Heal"])],
             "description": "Your healing spells cost 6% less mana."},
            {"pieces": 4, "equipEffects": [amod("Cost", -15, tags=["Totem"]),
                                           amod("Effect", 20, abilities=["shaman_healing_stream_totem"])],
             "description": "Your totems cost 15% less mana, and Healing Stream Totem heals for 20% more."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnHealCast", [
                {"type": "ApplyAura", "aura": "r1_aura_mossbloom", "target": "Target"}], chance=15, icd=15)}],
             "description": "Your heals have a 15% chance to make Mossbloom grow on the target, healing it for 22 every 3 sec for 9 sec. (15 sec cooldown.)"},
        ],
        "auras": [aura("r1_aura_mossbloom", "Mossbloom", "leaf", "Healing 22 every 3 sec.", 9,
                       tickInterval=3, tickEffects=[{"type": "Heal", "min": 22, "target": "Target"}])],
    },
    (1, "Hunter"): {
        "name": "Glowstalker's Garb", "key": "glowstalker",
        "pieces": [
            ("Glowstalker Hood", "mask", "Lined with glowcap spores, so you can see in the Hollow and the Hollow cannot see you."),
            ("Glowstalker Mantle", None, "A cape of firefly-silk. It flickers when your quarry is near."),
            ("Glowstalker Jerkin", None, "Tough leather, stitched with luminous thread in the pattern of a stag's track."),
            ("Glowstalker Handguards", None, "The fingertips are cut away. A good hunter feels the wind in the string."),
            ("Glowstalker Breeches", None, "Knees reinforced for kneeling in moss for a very long time, very quietly."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "RangedAttackPower", "value": 24}],
             "description": "+24 ranged attack power."},
            {"pieces": 4, "equipEffects": [amod("Damage", 10, tags=["Shot"])],
             "description": "Your Shots deal 10% more damage."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnRangedHit", [
                {"type": "ApplyAura", "aura": "r1_aura_glowstalker_focus", "target": "Self"}], chance=5, icd=30)}],
             "description": "Your ranged hits have a 5% chance to grant Lantern Focus: ranged attack speed increased by 15% for 12 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r1_aura_glowstalker_focus", "Lantern Focus", "eye", "Ranged attack speed increased by 15%.", 12,
                       [("RangedHaste", 15)])],
    },
    (1, "Rogue"): {
        "name": "Mothwing Leathers", "key": "mothwing",
        "pieces": [
            ("Mothwing Cowl", "mask", "Dusted with moth-scale powder. Lanterns forget you were ever in the room."),
            ("Mothwing Shoulderpads", None, "Folded like wings at rest. They open, a little, when you run."),
            ("Mothwing Vest", None, "It drinks lamplight and gives nothing back but a shadow shaped like you."),
            ("Mothwing Gloves", None, "So soft you could pick a lock, a pocket or a quarrel without anyone noticing."),
            ("Mothwing Leggings", None, "Silent on stone, silent on moss, mildly squeaky on stairs. Nobody is perfect."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("CostFlat", -5, tags=["Builder"])],
             "description": "Your combo point builders cost 5 less energy."},
            {"pieces": 4, "equipEffects": [amod("Damage", 15, abilities=["rogue_eviscerate", "rogue_rupture"])],
             "description": "Eviscerate and Rupture deal 15% more damage."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnFinisher", [
                {"type": "GainResource", "resource": "Energy", "amount": 25, "target": "Self"}], chance=25, icd=20)}],
             "description": "Your finishing moves have a 25% chance to give you the Moth's Second Wind, restoring 25 energy. (20 sec cooldown.)"},
        ],
        "auras": [],
    },
    (1, "Mage"): {
        "name": "Wickweaver's Regalia", "key": "wickweaver",
        "pieces": [
            ("Wickweaver Hood", "mask", "Embroidered with a single flame that turns to follow your thoughts."),
            ("Wickweaver Mantle", None, "Its tassels are old lamp-wicks, each still faintly warm."),
            ("Wickweaver Robes", None, "Woven from the threads of a thousand snuffed candles. Every one of them remembers burning."),
            ("Wickweaver Gloves", None, "Singed only in the places a careful mage would never admit to."),
            ("Wickweaver Trousers", None, "Lined with spark-proof silk, a lesson learned the hard way."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cost", -8, abilities=["mage_fireball", "mage_frostbolt"])],
             "description": "Fireball and Frostbolt cost 8% less mana."},
            {"pieces": 4, "equipEffects": [amod("CritChance", 3, schools=["Fire", "Frost"])],
             "description": "Your Fire and Frost spells have a 3% higher chance to critically strike."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnSpellCrit", [
                {"type": "ApplyAura", "aura": "r1_aura_wickflare", "target": "Self"}], chance=50, icd=30)}],
             "description": "Your spell critical strikes have a 50% chance to flare the wick: spell damage increased by 25 for 10 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r1_aura_wickflare", "Wickflare", "fire", "Spell damage increased by 25.", 10, [("SpellDamage", 25)])],
    },
    (1, "Warlock"): {
        "name": "Hollowshade Attire", "key": "hollowshade",
        "pieces": [
            ("Hollowshade Cowl", "mask", "The shadow inside the hood is a little deeper than your face strictly needs."),
            ("Hollowshade Mantle", None, "Something in the Hollow whispered into the seams. It is mostly polite."),
            ("Hollowshade Robe", None, "Dyed in the ink of the Hollow's black pools. It smells of wet roots and old promises."),
            ("Hollowshade Handwraps", None, "Wrapped tight, as if the hands inside might wander off and do something clever."),
            ("Hollowshade Leggings", None, "Cool to the touch, like a cellar in summer, or a bargain you should not have made."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Damage", 10, abilities=["warlock_corruption", "warlock_curse_of_agony"])],
             "description": "Corruption and Curse of Agony deal 10% more damage."},
            {"pieces": 4, "equipEffects": [amod("Effect", 20, abilities=["warlock_life_tap"])],
             "description": "Life Tap converts 20% more health into mana."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnPeriodicDamage", [
                {"type": "ApplyAura", "aura": "r1_aura_gloamfire", "target": "Self"}], chance=4, icd=30)}],
             "description": "Your damage over time has a 4% chance per tick to kindle Gloamfire: Shadow spell damage increased by 30 for 12 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r1_aura_gloamfire", "Gloamfire", "shadow", "Shadow spell damage increased by 30.", 12,
                       [("SpellDamage", 30, "Shadow")])],
    },
    # ------------------------------------------------------------------ tier 2: Ashwyrm's Roost
    (2, "Warrior"): {
        "name": "Ashwrought Battlegear", "key": "ashwrought",
        "pieces": [
            ("Ashwrought Greathelm", "crown", "Forged in a drake's breath and quenched in snow. It still hisses in the rain."),
            ("Ashwrought Shoulderguards", None, "Crested with the scales the Dragonsworn wore as trophies. Now they are yours."),
            ("Ashwrought Hauberk", None, "Every ring was hammered on the Roost's anvil-stone, where the wind never stops."),
            ("Ashwrought Gauntlets", None, "Heavy enough to stop a blade, nimble enough to pour the tea after."),
            ("Ashwrought Legguards", None, "The knees are scorched where someone once knelt before Vyrmathra. You will not."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "Dodge", "value": 2}],
             "description": "+2% chance to dodge."},
            {"pieces": 4, "equipEffects": [amod("Cooldown", -1, abilities=["warrior_shield_block"]),
                                           amod("Damage", 20, abilities=["warrior_revenge"])],
             "description": "The cooldown of Shield Block is reduced by 1 sec, and Revenge deals 20% more damage."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnBlock", [
                {"type": "Damage", "min": 60, "max": 80, "school": "Fire", "target": "Attacker", "threat": 120}],
                chance=30, icd=15)}],
             "description": "When you block, a 30% chance of an Ember Retort: 60 to 80 Fire damage to the attacker, with extra threat. (15 sec cooldown.)"},
        ],
        "auras": [],
    },
    (2, "Paladin"): {
        "name": "Dawnforge Regalia", "key": "dawnforge",
        "pieces": [
            ("Dawnforge Helm", "crown", "Polished to catch the first light over Skyreach. Ash slides off it like rain off a leaf."),
            ("Dawnforge Pauldrons", None, "Shaped like a folded sunrise. They are warm before you are."),
            ("Dawnforge Chestguard", None, "A lantern flame is etched at its heart, and it does not flicker in any wind."),
            ("Dawnforge Gauntlets", None, "Raised in blessing or in defence: the gauntlets cannot tell the difference, and neither can the light."),
            ("Dawnforge Legguards", None, "Built for the long climb to the Roost, and the longer stand at the top."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "Block", "value": 3}],
             "description": "+3% chance to block."},
            {"pieces": 4, "equipEffects": [amod("Cost", -10, tags=["Heal"]), amod("Healing", 10, tags=["Heal"])],
             "description": "Holy Light and Flash of Light cost 10% less mana and heal for 10% more."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnStruck", [
                {"type": "ApplyAura", "aura": "r2_aura_dawnforge_aegis", "target": "Self"}], chance=6, icd=30)}],
             "description": "When struck in melee, a 6% chance to raise the Dawnforged Aegis, absorbing 450 damage for 12 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r2_aura_dawnforge_aegis", "Dawnforged Aegis", "sun", "Absorbs 450 damage.", 12,
                       absorb={"amount": 450})],
    },
    (2, "Priest"): {
        "name": "Vestments of the Ember Choir", "key": "emberchoir",
        "pieces": [
            ("Emberchoir Circlet", "crown", "A band of rose-gold that hums the opening note of an old dawn hymn."),
            ("Emberchoir Amice", None, "Ember-red silk, embroidered with the open hands of the Choir."),
            ("Emberchoir Robe", None, "When you sing, the hem glows. When you heal, so does everyone near you."),
            ("Emberchoir Handwraps", None, "Ash-grey wraps that never stain, however many wounds you tend."),
            ("Emberchoir Leggings", None, "Soft as a hymn sung under the breath, so as not to wake a dragon."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "SpiritRegenWhileCasting", "value": 15}],
             "description": "Allows 15% of your mana regeneration from Spirit to continue while casting."},
            {"pieces": 4, "equipEffects": [amod("CritChance", 5, abilities=["priest_heal", "priest_flash_heal", "priest_prayer_of_healing"])],
             "description": "Heal, Flash Heal and Prayer of Healing have a 5% higher chance to critically heal."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnHealCast", [
                {"type": "ApplyAura", "aura": "r2_aura_ember_benediction", "target": "Target"}], chance=15, icd=15)}],
             "description": "Your heals have a 15% chance to lay an Ember Benediction on the target, healing it for 26 every 3 sec for 12 sec. (15 sec cooldown.)"},
        ],
        "auras": [aura("r2_aura_ember_benediction", "Ember Benediction", "renew", "Healing 26 every 3 sec.", 12, school="Holy",
                       tickInterval=3, tickEffects=[{"type": "Heal", "min": 26, "target": "Target"}])],
    },
    (2, "Shaman"): {
        "name": "Stormhearth Harness", "key": "stormhearth",
        "pieces": [
            ("Stormhearth Headdress", "mask", "Eagle feathers from the peaks, bound with copper wire that crackles before storms."),
            ("Stormhearth Shoulderwraps", None, "The four elements are knotted into the fringe. Earth insists on going first."),
            ("Stormhearth Tunic", None, "Thick yak leather, warm as a mountain hut with the kettle on."),
            ("Stormhearth Grips", None, "Rain beads on them even indoors. The spirits like to stay close."),
            ("Stormhearth Leggings", None, "Padded for kneeling at totems in the snow. The snow is grateful too."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("CastTime", -0.25, abilities=["shaman_healing_wave"])],
             "description": "The casting time of Healing Wave is reduced by 0.25 sec."},
            {"pieces": 4, "equipEffects": [amod("Effect", 25, abilities=["shaman_mana_spring_totem", "shaman_healing_stream_totem"])],
             "description": "Mana Spring Totem and Healing Stream Totem are 25% more effective."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnHealCast", [
                {"type": "Heal", "min": 70, "max": 90, "target": "Target", "chainTargets": 2, "chainFalloffPct": 25}],
                chance=10, icd=20)}],
             "description": "Your heals have a 10% chance to set off a Hearthfire Cascade, healing the target for 70 to 90 and jumping to 2 more allies. (20 sec cooldown.)"},
        ],
        "auras": [],
    },
    (2, "Hunter"): {
        "name": "Wyrmstalker's Garb", "key": "wyrmstalker",
        "pieces": [
            ("Wyrmstalker Cap", "mask", "Topped with a harpy's tail feather. The harpy lost a bet."),
            ("Wyrmstalker Spaulders", None, "Drake-hide plates sewn on soft leather, light enough for a long stalk."),
            ("Wyrmstalker Jerkin", None, "It smells of pine smoke and dragon. Wolves give you a wide and respectful berth."),
            ("Wyrmstalker Gloves", None, "The draw-finger is reinforced with a sliver of wyrm-horn."),
            ("Wyrmstalker Breeches", None, "Snow-pale leather for the high passes, where the drakes hunt from above."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Damage", 20, tags=["Sting"])],
             "description": "Your Stings deal 20% more damage."},
            {"pieces": 4, "equipEffects": [amod("Cooldown", -1, abilities=["hunter_arcane_shot", "hunter_multi_shot"]),
                                           amod("Cooldown", -120, abilities=["hunter_rapid_fire"])],
             "description": "The cooldowns of Arcane Shot and Multi-Shot are reduced by 1 sec, and of Rapid Fire by 2 min."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnRangedHit", [
                {"type": "Damage", "min": 70, "max": 100, "school": "Fire", "apCoef": 0.1, "target": "Target"}],
                chance=6, icd=20)}],
             "description": "Your ranged hits have a 6% chance to loose a Wyrmfire Arrow for 70 to 100 Fire damage. (20 sec cooldown.)"},
        ],
        "auras": [],
    },
    (2, "Rogue"): {
        "name": "Cinderveil Leathers", "key": "cinderveil",
        "pieces": [
            ("Cinderveil Mask", "mask", "A smoke-grey veil. Through it, the world is all embers and exits."),
            ("Cinderveil Shoulderpads", None, "They shed ash as you move, so your footsteps are always someone else's problem."),
            ("Cinderveil Vest", None, "Lined with drake-hide and patience. Arrows slide off it, and so do questions."),
            ("Cinderveil Gloves", None, "Fireproof, which is useful when the pocket you are picking belongs to a dragon."),
            ("Cinderveil Leggings", None, "Darkened in soot, quiet as falling ash."),
        ],
        "bonuses": [
            {"pieces": 2, "stats": [{"stat": "MeleeCrit", "value": 1}],
             "description": "+1% chance to critically strike with melee attacks."},
            {"pieces": 4, "equipEffects": [amod("DurationPct", 20, abilities=["rogue_slice_and_dice"]),
                                           amod("CritChance", 5, abilities=["rogue_sinister_strike", "rogue_backstab"])],
             "description": "Slice and Dice lasts 20% longer, and Sinister Strike and Backstab have a 5% higher chance to critically strike."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnMeleeCrit", [
                {"type": "ApplyAura", "aura": "r2_aura_cinder_smoulder", "target": "Target"}], chance=20, icd=15)}],
             "description": "Your melee critical strikes have a 20% chance to leave the target Smouldering: 20 Fire damage every 3 sec for 9 sec. (15 sec cooldown.)"},
        ],
        "auras": [aura("r2_aura_cinder_smoulder", "Smouldering", "ember", "20 Fire damage every 3 sec.", 9, kind="Debuff",
                       school="Fire", tickInterval=3,
                       tickEffects=[{"type": "Damage", "min": 20, "school": "Fire", "target": "Target"}])],
    },
    (2, "Mage"): {
        "name": "Skyflame Regalia", "key": "skyflame",
        "pieces": [
            ("Skyflame Circlet", "crown", "A thin crown of sky-blue fire. It is cold, which annoys the fire mages."),
            ("Skyflame Mantle", None, "The embroidered constellations rearrange themselves when nobody is looking."),
            ("Skyflame Robes", None, "Woven at the top of the world, where the air is thin and the spells are thick."),
            ("Skyflame Gloves", None, "Every fingertip is a small lantern. Gesture carefully."),
            ("Skyflame Trousers", None, "They billow dramatically, even in still air. Mages insist this is essential."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cost", -10, abilities=["mage_arcane_missiles", "mage_arcane_explosion", "mage_blizzard"])],
             "description": "Arcane Missiles, Arcane Explosion and Blizzard cost 10% less mana."},
            {"pieces": 4, "equipEffects": [amod("CritBonus", 20, schools=["Fire", "Frost", "Arcane"])],
             "description": "Your spell critical strikes deal 20% more bonus damage."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnSpellCast", [
                {"type": "ApplyAura", "aura": "r2_aura_skyflame_clarity", "target": "Self"}], chance=5, icd=30)}],
             "description": "Your spells have a 5% chance to grant Skyflame Clarity: mana costs reduced by 40% and casting speed increased by 15% for 10 sec. (30 sec cooldown.)"},
        ],
        "auras": [aura("r2_aura_skyflame_clarity", "Skyflame Clarity", "sparkle",
                       "Mana costs reduced by 40%. Casting speed increased by 15%.", 10, [("ManaCost", -40), ("CastSpeed", 15)])],
    },
    (2, "Warlock"): {
        "name": "Ashbinder's Raiment", "key": "ashbinder",
        "pieces": [
            ("Ashbinder Hood", "mask", "The ash of a dragon's last breath, bound into cloth by a contract of nine hundred clauses."),
            ("Ashbinder Mantle", None, "Small imps have embroidered rude words inside the lining. They are very proud of them."),
            ("Ashbinder Robe", None, "It smoulders at the edges and never burns down. Some fires are patient."),
            ("Ashbinder Handwraps", None, "Sooty fingerprints appear on everything you touch, then vanish as if ashamed."),
            ("Ashbinder Leggings", None, "Warm as a banked hearth, and just as likely to flare if poked."),
        ],
        "bonuses": [
            {"pieces": 2, "equipEffects": [amod("Cost", -8, abilities=["warlock_shadow_bolt"])],
             "description": "Shadow Bolt costs 8% less mana."},
            {"pieces": 4, "equipEffects": [amod("Damage", 8, tags=["Destruction"])],
             "description": "Your Destruction spells deal 8% more damage."},
            {"pieces": 5, "equipEffects": [{"type": "Proc", "proc": proc("OnSpellCrit", [
                {"type": "Damage", "min": 70, "max": 100, "school": "Fire", "coef": 0.2, "target": "Target"}],
                chance=25, icd=25)}],
             "description": "Your spell critical strikes have a 25% chance to sear a Cinderbrand into the target for 70 to 100 Fire damage. (25 sec cooldown.)"},
        ],
        "auras": [],
    },
}


def item(iid, name, icon, desc, kind, q, ilvl, req, equip, armor_type=None, weapon=None, armor=None, block=None,
         dmg=None, speed=None, school=None, stats=None, effects=None, classes=None, unique=False):
    d = {"id": iid, "name": name, "icon": icon, "description": desc, "kind": kind, "quality": q, "itemLevel": ilvl,
         "requiredLevel": req, "equip": equip}
    if armor_type:
        d["armorType"] = armor_type
    if weapon:
        d["weaponType"] = weapon
    if armor:
        d["armor"] = armor
    if block:
        d["block"] = block
    if dmg:
        d["minDamage"], d["maxDamage"], d["speed"] = dmg[0], dmg[1], speed
    if school:
        d["damageSchool"] = school
    d["stats"] = stats or []
    if effects:
        d["equipEffects"] = effects
    d["price"] = price(ilvl, equip, q)
    if classes:
        d["classes"] = classes
    if unique:
        d["unique"] = True
    return d


def build_tier(tier):
    t = TIERS[tier]
    items, sets, auras = [], [], []
    for cls in ["Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Shaman", "Mage", "Warlock"]:
        s = SETS[(tier, cls)]
        prof = PROFILES[cls.lower()]
        ids = []
        for slot, (name, icon, desc) in zip(SLOTS, s["pieces"]):
            ilvl = t["ilvl"][slot]
            fixed = [T2_EXTRA[cls]] if tier == 2 and cls in T2_EXTRA and slot in ("Head", "Chest") else []
            stats = spend(stat_budget(ilvl, "Epic", slot), prof, fixed)
            iid = f"{t['prefix']}_set_{cls.lower()}_{SLOT_KEY[slot]}"
            ids.append(iid)
            items.append(item(iid, name, icon or SLOT_ICON[slot], desc, "Armor", "Epic", ilvl, t["req"], slot,
                              armor_type=ARMOR_OF[cls], armor=armor_value(ARMOR_OF[cls], slot, ilvl, "Epic"),
                              stats=stats, classes=[cls]))
        sets.append({"id": f"set_{t['prefix']}_{cls.lower()}", "name": s["name"], "items": ids, "bonuses": s["bonuses"]})
        auras.extend(s["auras"])
    return items, sets, auras


# ------------------------------------------------------------------------------------------------ raid epics

WEAPON_ICON = {"Dagger": "dagger", "FistWeapon": "fist", "OneHandAxe": "axe", "OneHandMace": "mace",
               "OneHandSword": "sword", "Polearm": "spear", "Staff": "staff", "TwoHandAxe": "axe",
               "TwoHandMace": "hammer", "TwoHandSword": "sword", "Bow": "bow", "Crossbow": "bow", "Gun": "gun",
               "Thrown": "daggers", "Wand": "sparkle", "Shield": "shield"}
EQUIP_OF = {"Dagger": "OneHand", "FistWeapon": "OneHand", "OneHandAxe": "OneHand", "OneHandMace": "OneHand",
            "OneHandSword": "OneHand", "Polearm": "TwoHand", "Staff": "TwoHand", "TwoHandAxe": "TwoHand",
            "TwoHandMace": "TwoHand", "TwoHandSword": "TwoHand", "Bow": "Ranged", "Crossbow": "Ranged",
            "Gun": "Ranged", "Thrown": "Ranged", "Wand": "Ranged", "Shield": "OffHand", "HeldInOffhand": "OffHand"}
ACCESSORY_ICON = {"Neck": "hands_pray", "Finger": "halo", "Trinket": "coin", "Back": "wings"}


def gear(iid, name, desc, q, ilvl, req, kind, profile, fixed=(), speed=None, school=None, icon=None, effects=None,
         classes=None, unique=False, off_hand=False):
    """kind: a WeaponType, HeldInOffhand, or an accessory/armour slot (Neck, Finger, Trinket, Back).
    off_hand: a one-hand melee weapon made an "Off Hand" weapon (equip OffHand: off-hand slot only, needs dual wield);
    its DPS follows the one-hand rule, its stats the OffHand slot budget (0.56, a little richer than One-Hand 0.45).
    Stat equip effects are paid from the stat budget too (the guardrail test counts them)."""
    if off_hand:
        assert EQUIP_OF.get(kind) == "OneHand", f"{iid}: only one-hand melee weapons can be Off Hand weapons"
    equip_of = "OffHand" if off_hand else EQUIP_OF.get(kind, kind)
    budget = stat_budget(ilvl, q, equip_of)
    budget -= sum(e["value"] * stat_cost(e["stat"]) for e in (effects or []) if e["type"] == "Stat")
    stats = spend(budget, PROFILES[profile], fixed)
    if kind in EQUIP_OF:
        equip = equip_of
        if kind == "Shield":
            return item(iid, name, icon or "shield", desc, "Weapon", q, ilvl, req, equip, weapon="Shield",
                        armor=cs_round(30 * ilvl * 1.1 * (1.2 if q == "Legendary" else 1.0)),
                        block=cs_round((0.6 * ilvl + 3) * (1.2 if q == "Legendary" else 1.0)),
                        stats=stats, effects=effects, classes=classes, unique=unique)
        if kind == "HeldInOffhand":
            return item(iid, name, icon or "ember", desc, "Weapon", q, ilvl, req, equip, weapon="HeldInOffhand",
                        stats=stats, effects=effects, classes=classes, unique=unique)
        dps = weapon_dps(kind, ilvl, q) * DPS_RATIO[q]
        dmg = (cs_round(dps * speed * 0.75), cs_round(dps * speed * 1.25))
        return item(iid, name, icon or WEAPON_ICON[kind], desc, "Weapon", q, ilvl, req, equip, weapon=kind, dmg=dmg,
                    speed=speed, school=school, stats=stats, effects=effects, classes=classes, unique=unique)
    if kind == "Back":
        return item(iid, name, icon or "wings", desc, "Armor", q, ilvl, req, "Back", armor_type="Cloth",
                    armor=armor_value("Cloth", "Back", ilvl, q), stats=stats, effects=effects, classes=classes,
                    unique=unique)
    return item(iid, name, icon or ACCESSORY_ICON[kind], desc, "Accessory", q, ilvl, req, kind, stats=stats,
                effects=effects, classes=classes, unique=unique)


def proc_effect(p, desc):
    return [{"type": "Proc", "description": desc, "proc": p}]


# boss -> item ilvl, per raid
R1_ILVL = {"thornmaw": 26, "twins": 27, "mother_mire": 27, "hollow_heart": 28}
R2_ILVL = {"frostclaw": 36, "cinder_drakes": 37, "varkas": 37, "vyrmathra": 38}


def r1(boss, *a, **k):
    return boss, gear(a[0], a[1], a[2], "Epic", R1_ILVL[boss], 20, *a[3:], **k)


def r2(boss, *a, **k):
    return boss, gear(a[0], a[1], a[2], "Epic", R2_ILVL[boss], 30, *a[3:], **k)


EPICS = [
    # ------------------------------------------------------------------ Hollow Heart
    r1("thornmaw", "r1_ep_thornmaws_fang", "Thornmaw's Fang",
       "Pulled from the great bramble-beast's jaw. It still drips a little sap when it is hungry.",
       "Dagger", "agi_melee", speed=1.8),
    r1("thornmaw", "r1_ep_briarback_cleaver", "Briarback Cleaver",
       "A thorn the size of a door, sharpened. Thornmaw grew it; you are merely borrowing it.",
       "TwoHandAxe", "str_melee", fixed=[("MeleeCrit", 1)], speed=3.5),
    r1("thornmaw", "r1_ep_sporeweave_cloak", "Sporeweave Cloak",
       "Woven from the soft glowcap threads of the Hollow floor. It gives off a comfortable light, and a faint smell of mushrooms.",
       "Back", "tank"),
    r1("thornmaw", "r1_ep_wickglass_band", "Wickglass Band",
       "A ring of lantern glass with a flame trapped inside. It is perfectly content in there.",
       "Finger", "caster"),
    r1("twins", "r1_ep_sorrows_edge", "Sorrow's Edge",
       "The weeping twin's blade. It is always a little damp, as if it had just finished crying.",
       "OneHandSword", "hybrid_melee", speed=2.6),
    r1("twins", "r1_ep_solaces_tear", "Solace's Tear",
       "A single tear from the gentler twin, held in crystal. Everyone nearby feels slightly better about everything.",
       "Wand", "healer", speed=1.7, school="Holy"),
    r1("twins", "r1_ep_mossgrip_knuckles", "Mossgrip Knuckles",
       "Moss grows on them in the shape of a smile. They punch politely, but often.",
       "FistWeapon", "agi_melee", speed=2.5),
    r1("twins", "r1_ep_solaces_edge", "Solace's Edge",
       "Sorrow's blade had a sister. This one is always slightly warm, as if it had just finished giving someone a hug. "
       "Carry them together and the twins are, at last, on the same side.",
       "OneHandSword", "hybrid_melee", speed=2.4, off_hand=True),
    r1("twins", "r1_ep_twinned_locket", "Twinned Locket",
       "Two halves that never quite close. One holds a lock of dark hair, the other a lock of light.",
       "Neck", "healer"),
    r1("mother_mire", "r1_ep_mirelight_crook", "Mirelight Crook",
       "Mother Mire's own walking staff. The will-o'-wisp at its tip has changed sides, and is relieved about it.",
       "Staff", "healer", speed=3.0),
    r1("mother_mire", "r1_ep_bogiron_bludgeon", "Bog-Iron Bludgeon",
       "Forged from iron dredged up from the mire. It thunks rather than clangs, which unsettles enemies.",
       "OneHandMace", "tank", speed=2.6),
    r1("mother_mire", "r1_ep_witchlight_kris", "Witchlight Kris",
       "A wavy blade of green glass. The coven used it to carve their candles, and their grudges.",
       "Dagger", "caster", speed=1.8),
    r1("mother_mire", "r1_ep_hollowstring_longbow", "Hollowstring Longbow",
       "Strung with a root-fibre from the Hollow. It hums a low note just before every shot.",
       "Bow", "ranged", speed=2.8),
    r1("mother_mire", "r1_ep_rootbound_signet", "Rootbound Signet",
       "A thin root has grown around this ring and refuses to let go. Neither, it turns out, will you.",
       "Finger", "phys"),
    r1("mother_mire", "r1_ep_thornbrand_claymore", "Thornbrand Claymore",
       "Brambles have grown along the fuller. They bloom whenever it draws blood.",
       "TwoHandSword", "str_melee", speed=3.4),
    r1("hollow_heart", "r1_ep_heartroot_bulwark", "Heartroot Bulwark",
       "A cross-section of the Heart Tree itself, banded in iron. Count the rings if you like: it is very, very old.",
       "Shield", "tank"),
    r1("hollow_heart", "r1_ep_glowcap_lantern", "Glowcap Lantern",
       "A lantern full of glowing mushroom caps. They whisper spell-words to each other when they think you are asleep.",
       "HeldInOffhand", "caster", icon="ember"),
    r1("hollow_heart", "r1_ep_seed_of_the_hollow", "Seed of the Hollow",
       "The last seed of the corrupted heart, made clean again. It is warm, and beats very faintly.",
       "Trinket", "healer", icon="leaf",
       effects=proc_effect(proc("OnSpellCast", [{"type": "ApplyAura", "aura": "r1_aura_seedlight", "target": "Self"}],
                                chance=5, icd=45),
                           "Equip: your spells have a 5% chance to wake the Seedlight: spell damage increased by 30 and healing by 50 for 15 sec. (45 sec cooldown.)")),
    r1("hollow_heart", "r1_ep_bramblewick_charm", "Bramblewick Charm",
       "A twist of bramble around a lantern wick. It catches fire whenever you land a telling blow, then puts itself out, embarrassed.",
       "Trinket", "phys", icon="fire",
       effects=proc_effect(proc("OnCrit", [{"type": "ApplyAura", "aura": "r1_aura_bramble_fury", "target": "Self"}],
                                chance=25, icd=40),
                           "Equip: your critical strikes have a 25% chance to kindle Bramble Fury: attack power and ranged attack power increased by 60 for 15 sec. (40 sec cooldown.)")),
    # ------------------------------------------------------------------ Ashwyrm's Roost
    r2("frostclaw", "r2_ep_frostclaw_talon", "Frostclaw Talon",
       "Torn from the great ice-drake's foot. It is so cold that the wounds it makes forget to bleed for a moment.",
       "OneHandAxe", "hybrid_melee", speed=2.6),
    r2("frostclaw", "r2_ep_rimefang_dirk", "Rimefang Dirk",
       "Carved from one of Frostclaw's fangs. Frost mages say it hums in harmony with a good frostbolt.",
       "Dagger", "caster", speed=1.8),
    r2("frostclaw", "r2_ep_frostclaw_dewclaw", "Frostclaw's Dewclaw",
       "The small claw on the inside of the drake's foot, the one it keeps for delicate work. It is very good at "
       "delicate work.",
       "FistWeapon", "agi_melee", speed=2.0, off_hand=True),
    r2("frostclaw", "r2_ep_roostfeather_cloak", "Roost-Feather Cloak",
       "Sewn from drake feathers shed in the Roost's high nests. It catches the updraft whenever you leap.",
       "Back", "agi_melee"),
    r2("frostclaw", "r2_ep_hoarfrost_knives", "Hoarfrost Throwing Knives",
       "Six knives of blue ice that return to the bandolier on their own, eventually, if they feel like it.",
       "Thrown", "ranged", speed=2.0),
    r2("frostclaw", "r2_ep_emberwake_band", "Emberwake Band",
       "A gold ring that stays warm long after the fire has died. Healers swear it remembers every hand it has held.",
       "Finger", "healer"),
    r2("cinder_drakes", "r2_ep_emberjaw_maul", "Emberjaw Maul",
       "Made from Emberjaw's own jaw. It still closes on things, out of habit.",
       "TwoHandMace", "str_melee", fixed=[("MeleeCrit", 1)], speed=3.6),
    r2("cinder_drakes", "r2_ep_ashtongue_bellows_gun", "Ashtongue Bellows-Gun",
       "Built around the drake's own fire-gland. Every shot comes with a small, smug puff of smoke.",
       "Gun", "ranged", speed=2.8),
    r2("cinder_drakes", "r2_ep_cinderthorn_wand", "Cinderthorn Wand",
       "A spike of cooled magma. It is never quite cool.",
       "Wand", "caster", speed=1.7, school="Fire"),
    r2("cinder_drakes", "r2_ep_signet_of_twin_cinders", "Signet of Twin Cinders",
       "Two drake-embers set side by side, forever arguing about which of them is brighter.",
       "Finger", "caster"),
    r2("varkas", "r2_ep_highlords_warglaive", "Highlord's Warglaive",
       "Varkas carried it at the head of the Dragonsworn vanguard. It is heavy with the weight of bad decisions.",
       "Polearm", "hybrid_melee", fixed=[("MeleeCrit", 1)], speed=3.5),
    r2("varkas", "r2_ep_dragonsworn_arbalest", "Dragonsworn Arbalest",
       "Cranked by a little brass dragon on the stock, who glares at anyone else who touches it.",
       "Crossbow", "ranged", speed=3.0),
    r2("varkas", "r2_ep_oathbreaker", "Oathbreaker",
       "Varkas swore an oath on this blade to serve the Ashwyrm. The blade, it seems, had other plans.",
       "OneHandSword", "tank", speed=2.6),
    r2("varkas", "r2_ep_wyrmscale_choker", "Wyrmscale Choker",
       "A collar of overlapping scales. It tightens protectively when danger is near, which is touching, if a little choking.",
       "Neck", "tank"),
    r2("varkas", "r2_ep_roostkeepers_lantern", "Roostkeeper's Lantern",
       "The lantern the old monks of the Roost kept lit through the dragon's long sleep. It has still not gone out.",
       "Trinket", "healer", icon="ember",
       effects=proc_effect(proc("OnSpellCast", [{"type": "ApplyAura", "aura": "r2_aura_roostkeepers_glow", "target": "Self"}],
                                chance=5, icd=45),
                           "Equip: your spells have a 5% chance to fill you with the Roostkeeper's Glow: spell damage increased by 40 and healing by 70 for 15 sec. (45 sec cooldown.)")),
    r2("vyrmathra", "r2_ep_hoardgold_scepter", "Hoardgold Scepter",
       "Plucked from the top of Vyrmathra's hoard. The dragon had been using it to scratch her back.",
       "OneHandMace", "healer", speed=2.4),
    r2("vyrmathra", "r2_ep_skyfire_greatstaff", "Skyfire Greatstaff",
       "Its crystal head holds a sliver of the sky above the Roost: blue, endless, and slightly on fire.",
       "Staff", "caster", fixed=[("SpellCrit", 1)], speed=3.0),
    r2("vyrmathra", "r2_ep_ashen_starglobe", "Ashen Starglobe",
       "A glass globe of slowly swirling ash. Look long enough and the ash makes stars.",
       "HeldInOffhand", "healer", icon="star"),
    r2("vyrmathra", "r2_ep_heart_of_cinders", "Heart of Cinders",
       "Still warm from the dragon's breast. It beats faster when you fight, and so do you.",
       "Trinket", "phys", icon="fire",
       effects=proc_effect(proc("OnCrit", [{"type": "ApplyAura", "aura": "r2_aura_cinder_heart", "target": "Self"}],
                                chance=25, icd=40),
                           "Equip: your critical strikes have a 25% chance to quicken the Cinder Heart: attack power and ranged attack power increased by 90 for 15 sec. (40 sec cooldown.)")),
]

EPIC_AURAS = [
    aura("r1_aura_seedlight", "Seedlight", "leaf", "Spell damage increased by 30. Healing increased by 50.", 15,
         [("SpellDamage", 30), ("HealingPower", 50)]),
    aura("r1_aura_bramble_fury", "Bramble Fury", "rage", "Attack power and ranged attack power increased by 60.", 15,
         [("AttackPower", 60), ("RangedAttackPower", 60)]),
    aura("r2_aura_roostkeepers_glow", "Roostkeeper's Glow", "ember", "Spell damage increased by 40. Healing increased by 70.", 15,
         [("SpellDamage", 40), ("HealingPower", 70)]),
    aura("r2_aura_cinder_heart", "Cinder Heart", "fire", "Attack power and ranged attack power increased by 90.", 15,
         [("AttackPower", 90), ("RangedAttackPower", 90)]),
]

# ------------------------------------------------------------------------------------------------ legendaries

LEGENDARIES = [
    ("hollow_heart", gear(
        "r1_lg_kindlewood", "Kindlewood, Last Staff of the Heart Lantern",
        "When the Heart Lantern guttered, its last flame fled into a single root of the Hollow. Mother Mire hunted it "
        "for a hundred years. It was waiting for someone kinder.",
        "Legendary", 30, 20, "Staff", "caster", fixed=[("SpellCrit", 1)], speed=3.0,
        effects=proc_effect(proc("OnSpellCast", [{"type": "ApplyAura", "aura": "r1_lg_aura_kindled_soul", "target": "Self"}],
                                 chance=7, icd=25),
                            "Equip: your spells have a 7% chance to rekindle your Kindled Soul: spell damage increased by 45 and casting speed by 10% for 12 sec. (25 sec cooldown.)"),
        unique=True)),
    ("hollow_heart", gear(
        "r1_lg_solace", "Solace, the Twins' Lullaby",
        "Every night beneath the roots, Solace sang her sorrowing sister to sleep. The song never ended; it only moved "
        "into the mace, and now it sings for whoever you are holding up.",
        "Legendary", 30, 20, "OneHandMace", "healer", fixed=[("ManaRegen", 2)], speed=2.4,
        effects=proc_effect(proc("OnHealCast", [{"type": "Heal", "min": 80, "max": 100, "coef": 0.1,
                                                 "target": "AlliesInRadius", "radius": 12}], chance=10, icd=20),
                            "Equip: your heals have a 10% chance to sing the Lullaby of Solace, healing every ally within 12 yards for 80 to 100. (20 sec cooldown.)"),
        unique=True)),
    ("vyrmathra", gear(
        "r2_lg_embersong", "Embersong, Fang of Vyrmathra",
        "Forged from the fang Vyrmathra lost in her first battle, long before Lanternvale was a village. When it strikes "
        "true, you can hear her humming in her sleep.",
        "Legendary", 40, 30, "TwoHandSword", "str_melee", fixed=[("MeleeCrit", 1)], speed=3.5,
        effects=proc_effect(proc("OnMeleeHit", [
            {"type": "Damage", "min": 90, "max": 120, "school": "Fire", "target": "Target", "chainTargets": 2,
             "chainFalloffPct": 20},
            {"type": "ApplyAura", "aura": "r2_lg_aura_song_of_embers", "target": "Self"}], ppm=2, icd=4),
            "Chance on hit: a Wyrmfire Arc burns the target for 90 to 120 Fire damage and leaps to 2 more foes, and the Song of Embers raises your Strength by 30 for 12 sec."),
        unique=True)),
    ("vyrmathra", gear(
        "r2_lg_dawnstring", "Dawnstring, the Last Light of Skyreach",
        "Strung with a single thread of the first sunrise ever seen from the peaks. Every arrow it looses carries the "
        "morning with it.",
        "Legendary", 40, 30, "Bow", "ranged", speed=2.9,
        effects=proc_effect(proc("OnRangedHit", [
            {"type": "Damage", "min": 110, "max": 150, "school": "Holy", "apCoef": 0.1, "target": "Target"},
            {"type": "ApplyAura", "aura": "r2_lg_aura_dawnwatch", "target": "Self"}], chance=8, icd=20),
            "Chance on hit: a Dawnshot strikes for 110 to 150 Holy damage and grants Dawnwatch: ranged attack power increased by 70 and ranged attack speed by 10% for 12 sec. (20 sec cooldown.)"),
        unique=True)),
    ("vyrmathra", gear(
        "r2_lg_last_scale", "Vyrmathra's Last Scale",
        "The one scale over the Ashwyrm's heart, the one every legend says is missing. It was not missing. It was "
        "waiting to be carried home.",
        "Legendary", 40, 30, "Shield", "tank",
        effects=[{"type": "Stat", "stat": "ThreatGenerated", "value": 5, "description": "Equip: threat generated increased by 5%."}]
        + proc_effect(proc("OnBlock", [
            {"type": "ApplyAura", "aura": "r2_lg_aura_wyrmscale_ward", "target": "Self"},
            {"type": "Damage", "min": 60, "max": 80, "school": "Fire", "target": "Attacker", "threat": 200}],
            chance=35, icd=12),
            "When you block: a 35% chance to raise the Wyrmscale Ward, absorbing 600 damage for 10 sec, while the scale scorches the attacker for 60 to 80 Fire damage with great threat. (12 sec cooldown.)"),
        unique=True)),
]

LEGENDARY_AURAS = [
    aura("r1_lg_aura_kindled_soul", "Kindled Soul", "fire", "Spell damage increased by 45. Casting speed increased by 10%.", 12,
         [("SpellDamage", 45), ("CastSpeed", 10)]),
    aura("r2_lg_aura_song_of_embers", "Song of Embers", "ember", "Strength increased by 30.", 12, [("Strength", 30)]),
    aura("r2_lg_aura_dawnwatch", "Dawnwatch", "sun", "Ranged attack power increased by 70. Ranged attack speed increased by 10%.", 12,
         [("RangedAttackPower", 70), ("RangedHaste", 10)]),
    aura("r2_lg_aura_wyrmscale_ward", "Wyrmscale Ward", "shield", "Absorbs 600 damage.", 10, absorb={"amount": 600}),
]

# ------------------------------------------------------------------------------------------------ loot tables

BOSSES = {
    "r1": [("thornmaw", ["Hands"], 2000, 3000), ("twins", ["Shoulder"], 2000, 3000),
           ("mother_mire", ["Legs"], 2500, 3500), ("hollow_heart", ["Head", "Chest"], 4000, 6000)],
    "r2": [("frostclaw", ["Hands"], 4000, 5000), ("cinder_drakes", ["Shoulder"], 4000, 5000),
           ("varkas", ["Legs"], 4500, 6000), ("vyrmathra", ["Head", "Chest"], 8000, 12000)],
}
TRASH_GOLD = {"r1": (300, 600), "r2": (500, 900)}
LEGENDARY_CHANCE = 5
CLASS_ORDER = ["warrior", "paladin", "hunter", "rogue", "priest", "shaman", "mage", "warlock"]


def tables():
    out = []
    for raid, bosses in BOSSES.items():
        epic_ids_all = [it["id"] for b, it in EPICS if it["id"].startswith(raid + "_")]
        for boss, slots, gmin, gmax in bosses:
            entries = []
            for slot in slots:
                entries.append({"pool": [f"{raid}_set_{c}_{SLOT_KEY[slot]}" for c in CLASS_ORDER],
                                "perMembers": 5, "partyUsable": True})
            entries.append({"pool": [it["id"] for b, it in EPICS if b == boss and it["id"].startswith(raid + "_")],
                            "perMembers": 5, "partyUsable": True})
            for b, lg in LEGENDARIES:
                if b == boss:
                    entries.append({"pool": [lg["id"]], "chance": LEGENDARY_CHANCE, "skipOwned": True, "partyUsable": True})
            entries.append({"random": True, "chance": 100, "quality": "Rare", "itemLevelOffset": 3})
            out.append({"id": f"lt_{raid}_{boss}", "goldMin": gmin, "goldMax": gmax, "entries": entries})
        gmin, gmax = TRASH_GOLD[raid]
        out.append({"id": f"lt_{raid}_trash", "goldMin": gmin, "goldMax": gmax, "entries": [
            {"random": True, "chance": 30, "quality": "Uncommon", "itemLevelOffset": 2},
            {"random": True, "chance": 12, "quality": "Rare", "itemLevelOffset": 3},
            {"pool": epic_ids_all, "chance": 2, "partyUsable": True},
        ]})
    return out


# ------------------------------------------------------------------------------------------------ output

ITEM_ORDER = ["id", "name", "icon", "description", "kind", "quality", "itemLevel", "requiredLevel", "equip", "armorType",
              "weaponType", "armor", "block", "minDamage", "maxDamage", "speed", "damageSchool", "stats",
              "equipEffects", "use", "consumable", "stack", "price", "classes", "unique", "quest", "art"]


def compact(v):
    return json.dumps(v, ensure_ascii=False, separators=(", ", ": "))


def dump_obj(o, indent):
    pad = " " * indent
    keys = list(o.keys())
    if "id" in o and "itemLevel" in o:
        keys = [k for k in ITEM_ORDER if k in o]
    lines = []
    for k in keys:
        v = o[k]
        if k in ("entries", "bonuses") and isinstance(v, list) and len(v) > 1:
            inner = ",\n".join(f"{pad}    {compact(x)}" for x in v)
            lines.append(f'{pad}  {json.dumps(k)}: [\n{inner}\n{pad}  ]')
        else:
            lines.append(f'{pad}  {json.dumps(k)}: {compact(v)}')
    return pad + "{\n" + ",\n".join(lines) + "\n" + pad + "}"


def write(fname, note, sections):
    parts = ['{', f'  "_note": {json.dumps(note, ensure_ascii=False)},']
    sec_txt = []
    for key, objs in sections:
        body = ",\n".join(dump_obj(o, 4) for o in objs)
        sec_txt.append(f'  "{key}": [\n{body}\n  ]')
    parts.append(",\n".join(sec_txt))
    parts.append('}')
    path = os.path.join(OUT, fname)
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(parts) + "\n")
    json.load(open(path, encoding="utf-8"))  # sanity: valid JSON
    print("wrote", os.path.relpath(path, ROOT))


def report(items):
    """Budget summary: authored cost / authored target, and / ItemGenerator.StatBudget (the guardrail's ratio)."""
    gen_q = {"Epic": 1.6, "Legendary": 2.0}
    worst = (9, 0)
    for it in items:
        slot = it["equip"]
        cost = cost_of(it["stats"]) + sum(e["value"] * stat_cost(e["stat"]) for e in it.get("equipEffects", []) if e["type"] == "Stat")
        target = stat_budget(it["itemLevel"], it["quality"], slot)
        guard = cost / (0.55 * it["itemLevel"] * gen_q[it["quality"]] * SLOT_MULT[slot])
        worst = (min(worst[0], guard), max(worst[1], guard))
        if abs(cost / target - 1) > 0.12:
            print(f"  budget off: {it['id']} cost {cost:.1f} target {target:.1f}")
    print(f"  {len(items)} items, guardrail ratio range {worst[0]:.2f}..{worst[1]:.2f} (allowed 0.80..3.20)")


def main():
    note_sets = ("Raid tier {t} class sets ({raid}): 8 classes x head/shoulders/chest/hands/legs, Epic, required level {req}, "
                 "item level {lo}-{hi}, class-restricted, in each class's armour at that level, with 2/4/5-piece bonuses. "
                 "Generated by Tools/datagen/raid_loot/gen_raid_loot.py (budgets per Docs/Expansion.md §8). Owner: raidloot.")
    all_items = []
    for tier, raid in ((1, "Hollow Heart"), (2, "Ashwyrm's Roost")):
        items, sets, auras = build_tier(tier)
        all_items += items
        il = TIERS[tier]["ilvl"].values()
        sections = [("items", items), ("itemSets", sets)]
        if auras:
            sections.append(("auras", auras))
        write(f"raid_loot_t{tier}.json", note_sets.format(t=tier, raid=raid, req=TIERS[tier]["req"], lo=min(il), hi=max(il)),
              sections)
    epics = [it for _, it in EPICS] + [it for _, it in LEGENDARIES]
    all_items += epics
    write("raid_loot_epics.json",
          "Non-set raid epics of the Hollow Heart (r1_ep_*, required level 20, item level 26-28) and Ashwyrm's Roost "
          "(r2_ep_*, required level 30, item level 36-38), the five legendaries (r1_lg_*, r2_lg_*; unique) and their "
          "proc auras. Generated by Tools/datagen/raid_loot/gen_raid_loot.py. Owner: raidloot.",
          [("items", epics), ("auras", EPIC_AURAS + LEGENDARY_AURAS)])
    write("raid_loot.json",
          "Raid loot tables (Docs/Expansion.md §8 raid loot design). Bosses: one tier-set pool per slot (boss 1 hands, "
          "boss 2 shoulders, boss 3 legs, final head + chest) and the boss's epic pool, each perMembers 5 + "
          "partyUsable (a 10-raid gets 2 of each), legendaries skipOwned at 5% on the finals, gold and a Rare random. "
          "Trash: Uncommon/Rare randoms and a 2% raid epic. Generated by Tools/datagen/raid_loot/gen_raid_loot.py. "
          "Owner: raidloot.",
          [("lootTables", tables())])
    report(all_items)


if __name__ == "__main__":
    main()
