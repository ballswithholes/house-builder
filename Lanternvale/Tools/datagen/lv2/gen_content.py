"""Creatures, abilities, auras, loot, items, NPCs, dialogues and quests of the lv builder:
lv2_content.json (Lanternvale's northern band) and dgn_root_hollows_content.json (the Root Hollows)."""
import os
from items_lib import gear


# ====================================================================== dialogue helpers
def N(id, speaker, text, choices=None, conditions=None, fallback="", outcomes=None, next=""):
    n = {"id": id, "speaker": speaker, "text": text}
    if conditions:
        n["conditions"] = conditions
    if fallback:
        n["fallback"] = fallback
    if outcomes:
        n["outcomes"] = outcomes
    if choices is not None:
        n["choices"] = choices
    if next:
        n["next"] = next
    return n


def C(text, next="", conds=None, outs=None, check=None, once=False, tag=""):
    c = {"text": text}
    if next:
        c["next"] = next
    if conds:
        c["conditions"] = conds
    if outs:
        c["outcomes"] = outs
    if check:
        c["check"] = check
    if once:
        c["once"] = True
    if tag:
        c["tag"] = tag
    return c


def chk(skill, dc, s, f):
    return {"skill": skill, "dc": dc, "success": s, "failure": f}


def flag(k): return {"type": "Flag", "key": k}
def notflag(k): return {"type": "NotFlag", "key": k}
def qstate(q, s): return {"type": "QuestState", "key": q, "value": s}
def qnot(q): return {"type": "QuestNotStarted", "key": q}
def qactive(q): return {"type": "QuestActive", "key": q}
def qdone(q): return {"type": "QuestComplete", "key": q}
def level(n): return {"type": "Level", "amount": n}
def cls(c): return {"type": "Class", "key": c}
def has(i, n=1): return {"type": "HasItem", "key": i, "amount": n}
def setf(k): return {"type": "SetFlag", "key": k}
def start(q): return {"type": "StartQuest", "key": q}
def done(q): return {"type": "CompleteQuest", "key": q}
def xp(n): return {"type": "GiveXP", "amount": n}
def give(i, n=1): return {"type": "GiveItem", "key": i, "amount": n}
def take(i, n=1): return {"type": "TakeItem", "key": i, "amount": n}
def gold(n): return {"type": "GiveGold", "amount": n}
def fight(e): return {"type": "StartCombat", "key": e}


# ====================================================================== creatures
def creature(id, name, desc, sprite, type, rank, lmin, lmax, offset, floor, cap, size, ai, abilities, loot="", portrait="",
             family="", hm=None, dm=None, am=None, attack=2.0, move=8, material="", voice="", passives=None, immune=None,
             stats=None, ranged=False, rng=0, school="", resource="", xpMult=None, faction="", bark="", tameable=False):
    c = {"id": id, "name": name, "description": desc, "sprite": sprite}
    if portrait:
        c["portrait"] = portrait
    c["type"] = type
    if family:
        c["family"] = family
    c["rank"] = rank
    if resource:
        c["resource"] = resource
    c.update({"levelMin": lmin, "levelMax": lmax, "scaleToParty": True, "levelOffset": offset, "levelFloor": floor, "levelCap": cap})
    if hm is not None:
        c["healthMult"] = hm
    if dm is not None:
        c["damageMult"] = dm
    if am is not None:
        c["armorMult"] = am
    c["attackSpeed"] = attack
    if school:
        c["meleeSchool"] = school
    if ranged:
        c["ranged"] = True
        c["rangedRange"] = rng
        c["projectile"] = "fx_bolt"
    c.update({"moveSpeed": move, "size": size, "ai": ai, "abilities": abilities})
    if passives:
        c["passives"] = passives
    if immune:
        c["immune"] = immune
    if stats:
        c["stats"] = stats
    if loot:
        c["lootTable"] = loot
    if xpMult is not None:
        c["xpMult"] = xpMult
    if tameable:
        c["tameable"] = True
    if material:
        c["material"] = material
    if voice:
        c["voice"] = voice
    if faction:
        c["faction"] = faction
    if bark:
        c["bark"] = bark
    return c


def ab(a, p, cond="", chance=None):
    d = {"ability": a, "priority": p}
    if cond:
        d["condition"] = cond
    if chance is not None:
        d["chance"] = chance
    return d


# ---------------------------------------------------------------- shared root abilities (Root Hollows and the orchard)
ABILITIES_DG1 = [
    {"id": "cr_dg1_root_headbutt", "name": "Root Headbutt", "icon": "rock", "description": "Butts the target with a hard knot of root for weapon damage plus {0}.",
     "school": "Physical", "hidden": True, "cooldown": 6, "target": "Enemy", "melee": True,
     "effects": [{"type": "WeaponDamage", "weaponPct": 100, "min": 3, "max": 5, "perLevel": 0.6}], "aiHint": "Damage", "aiPriority": 3},
    {"id": "cr_dg1_sap_mend", "name": "Sap Mend", "icon": "leaf", "description": "Smears warm amber sap over an ally's wounds, healing {0}.",
     "school": "Nature", "hidden": True, "castTime": 2.0, "cooldown": 6, "target": "Ally", "range": 30,
     "effects": [{"type": "Heal", "min": 22, "max": 30, "perLevel": 3}], "aiHint": "Heal", "aiPriority": 8},
    {"id": "cr_dg1_amber_spit", "name": "Amber Spit", "icon": "nature", "description": "Spits a glob of hardened sap for {0} Nature damage.",
     "school": "Nature", "hidden": True, "castTime": 2.0, "target": "Enemy", "range": 30,
     "effects": [{"type": "Damage", "min": 7, "max": 10, "perLevel": 0.9}], "aiHint": "Damage", "aiPriority": 3},
    {"id": "cr_dg1_sticky_sap", "name": "Sticky Sap", "icon": "water_drop", "description": "Gums an enemy's feet with sap. Movement speed reduced by 50% for 9 sec.",
     "school": "Nature", "hidden": True, "cooldown": 18, "target": "Enemy", "range": 25,
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_sticky_sap"}], "aiHint": "Debuff", "aiPriority": 5},
    {"id": "cr_dg1_ashen_bolt", "name": "Ashen Bolt", "icon": "void", "description": "A mote of grey, ashy light for {0} Shadow damage.",
     "school": "Shadow", "hidden": True, "castTime": 2.0, "target": "Enemy", "range": 30,
     "effects": [{"type": "Damage", "min": 8, "max": 11, "perLevel": 0.9}], "aiHint": "Damage", "aiPriority": 3},
    {"id": "cr_dg1_gutter", "name": "Gutter", "icon": "curse", "description": "The wisp dims an enemy's inner light. Healing taken reduced by 25% for 12 sec.",
     "school": "Shadow", "hidden": True, "cooldown": 18, "target": "Enemy", "range": 30,
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_guttered"}], "aiHint": "Debuff", "aiPriority": 5},
    {"id": "cr_dg1_root_bind", "name": "Root Bind", "icon": "leaf", "description": "Roots burst from the floor and seize the target, rooting it for 9 sec.",
     "school": "Nature", "hidden": True, "castTime": 1.5, "cooldown": 18, "target": "Enemy", "range": 30,
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_root_bound"}], "aiHint": "CC", "aiPriority": 6},
    {"id": "cr_dg1_burrow_stomp", "name": "Burrow Stomp", "icon": "earth", "description": "Stamps hard enough to shake the burrow, dealing {0} damage to enemies within 6 yards.",
     "school": "Physical", "hidden": True, "cooldown": 12, "target": "Self",
     "area": {"shape": "Circle", "radius": 6, "centeredOnCaster": True, "affects": "Enemies"},
     "effects": [{"type": "Damage", "min": 10, "max": 15, "perLevel": 1.0}], "aiHint": "AoE", "aiPriority": 7},
    {"id": "cr_dg1_thick_bark", "name": "Thick Bark", "icon": "armor", "description": "Bark thickens over the body, increasing armor by 50% for 18 sec.",
     "school": "Nature", "hidden": True, "cooldown": 30, "target": "Self",
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_thick_bark", "target": "Self"}], "aiHint": "Buff", "aiPriority": 4},
    {"id": "cr_dg1_sentinel_slam", "name": "Sentinel Slam", "icon": "hammer", "description": "A slow, crushing root-fist that knocks the target back.",
     "school": "Physical", "hidden": True, "cooldown": 10, "target": "Enemy", "melee": True,
     "effects": [{"type": "WeaponDamage", "weaponPct": 125}, {"type": "Knockback", "distance": 6}], "aiHint": "Damage", "aiPriority": 5},
    {"id": "cr_dg1_ward_of_roots", "name": "Ward of Roots", "icon": "shield", "description": "Wraps an ally in living roots, increasing its armor by 50% for 18 sec.",
     "school": "Nature", "hidden": True, "cooldown": 30, "target": "Ally", "range": 20,
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_thick_bark"}], "aiHint": "Buff", "aiPriority": 4},
    # the Rootwarden
    {"id": "cr_dg1_root_slam", "name": "Root Slam", "icon": "hammer", "description": "The Rootwarden brings both root-claws down on its target.",
     "school": "Physical", "hidden": True, "cooldown": 8, "target": "Enemy", "melee": True,
     "effects": [{"type": "WeaponDamage", "weaponPct": 130}], "aiHint": "Damage", "aiPriority": 5},
    {"id": "cr_dg1_sweeping_claws", "name": "Sweeping Claws", "icon": "claw", "description": "A wide sweep of root-claws, dealing {0} damage to enemies within 7 yards.",
     "school": "Physical", "hidden": True, "cooldown": 12, "target": "Self",
     "area": {"shape": "Circle", "radius": 7, "centeredOnCaster": True, "affects": "Enemies"},
     "effects": [{"type": "Damage", "min": 18, "max": 24, "perLevel": 1.6}], "aiHint": "AoE", "aiPriority": 7},
    {"id": "cr_dg1_strangling_roots", "name": "Strangling Roots", "icon": "leaf", "description": "Grey roots coil round the target, rooting it and dealing Nature damage every 3 sec for 12 sec.",
     "school": "Nature", "hidden": True, "castTime": 1.5, "cooldown": 18, "target": "Enemy", "range": 30,
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_strangled"}], "aiHint": "CC", "aiPriority": 6},
    {"id": "cr_dg1_ash_bloom", "name": "Ash-Seed Bloom", "icon": "void",
     "description": "The grey seed in the Rootwarden's heart blooms. After a long cast, deals {0} Shadow damage to every enemy in the hollow and leaves them Ashen.",
     "school": "Shadow", "hidden": True, "castTime": 4.0, "cooldown": 24, "target": "Self",
     "area": {"shape": "Circle", "radius": 40, "centeredOnCaster": True, "affects": "Enemies"},
     "effects": [{"type": "Damage", "min": 26, "max": 34, "perLevel": 2.4}, {"type": "ApplyAura", "aura": "cr_dg1_ashen"}],
     "tags": ["Telegraph"], "aiHint": "AoE", "aiPriority": 9},
    {"id": "cr_dg1_call_sprouts", "name": "Call the Sprouts", "icon": "leaf", "description": "The Rootwarden shakes its antlers and two of Kusu's sprouts drop out of the roof.",
     "school": "Nature", "hidden": True, "cooldown": 600, "target": "Self",
     "effects": [{"type": "Summon", "summon": "cr_dg1_sprout", "count": 2, "target": "Self"}], "aiHint": "Summon", "aiPriority": 10},
    {"id": "cr_dg1_lantern_blaze", "name": "Lantern Heart Blaze", "icon": "fire", "description": "Below 30% health the lantern heart blazes against the seed.",
     "school": "Fire", "hidden": True, "time": "OffGcd", "cooldown": 600, "target": "Self",
     "effects": [{"type": "ApplyAura", "aura": "cr_dg1_lantern_blaze", "target": "Self"}], "aiHint": "Buff", "aiPriority": 10},
]

AURAS_DG1 = [
    {"id": "cr_dg1_sticky_sap", "name": "Sticky Sap", "icon": "water_drop", "kind": "Debuff", "school": "Nature", "dispel": "Magic", "duration": 9,
     "states": ["Daze"], "description": "Movement speed reduced by 50%.", "mods": [{"stat": "MoveSpeed", "value": -50, "pct": True}]},
    {"id": "cr_dg1_guttered", "name": "Guttered", "icon": "curse", "kind": "Debuff", "school": "Shadow", "dispel": "Curse", "duration": 12,
     "description": "Your light gutters. Healing taken reduced by 25%.", "mods": [{"stat": "HealingTaken", "value": -25, "pct": True}]},
    {"id": "cr_dg1_root_bound", "name": "Root Bound", "icon": "leaf", "kind": "Debuff", "school": "Nature", "dispel": "Magic", "duration": 9,
     "states": ["Root"], "description": "Rooted."},
    {"id": "cr_dg1_thick_bark", "name": "Thick Bark", "icon": "armor", "kind": "Buff", "school": "Nature", "dispel": "Magic", "duration": 18,
     "description": "Armor increased by 50%.", "mods": [{"stat": "Armor", "value": 50, "pct": True}]},
    {"id": "cr_dg1_strangled", "name": "Strangling Roots", "icon": "leaf", "kind": "Debuff", "school": "Nature", "dispel": "Magic", "duration": 12,
     "tickInterval": 3, "states": ["Root"], "description": "Rooted. Nature damage every 3 sec.", "tickEffects": [{"type": "Damage", "min": 6, "perLevel": 0.8}]},
    {"id": "cr_dg1_ashen", "name": "Ashen", "icon": "void", "kind": "Debuff", "school": "Shadow", "dispel": "Magic", "duration": 12,
     "tickInterval": 3, "description": "Grey ash in the lungs. Shadow damage every 3 sec.", "tickEffects": [{"type": "Damage", "min": 3, "perLevel": 0.5}]},
    {"id": "cr_dg1_lantern_blaze", "name": "Lantern Heart Blaze", "icon": "fire", "kind": "Buff", "school": "Fire", "duration": 0,
     "description": "The lantern heart blazes. Damage dealt and attack speed increased by 20%.",
     "mods": [{"stat": "DamageDone", "value": 20, "pct": True}, {"stat": "MeleeHaste", "value": 20, "pct": True}]},
    {"id": "cr_dg1_ash_touched", "name": "Ash-Touched", "icon": "void", "kind": "Buff", "school": "Shadow", "hidden": True,
     "description": "Touched by the grey seed. Resistant to Shadow.", "mods": [{"stat": "Resistance", "value": 10, "school": "Shadow"}]},
]

CREATURES_DG1 = [
    creature("cr_dg1_rootling", "Rootling", "One of Old Kusu's little sprouts, walking about on its own roots. Its eyes have gone from amber to ash-grey.",
             "cr_rootling", "Elemental", "Normal", 11, 13, 1, 11, 13, 0.9, "Melee", [ab("cr_dg1_root_headbutt", 4)], loot="lt_dg1_rootling",
             portrait="cr_mossling", hm=0.95, move=8, material="wood", voice="wood", passives=["cr_dg1_ash_touched"], faction="Kusu's Roots",
             bark="*Creak. Creak-creak. The rootling's ash-grey eyes fix on you.*"),
    creature("cr_dg1_sapcaller", "Rootling Sapcaller", "An older rootling with a glowing bud on its crown. It mends the others with amber sap.",
             "cr_rootling", "Elemental", "Normal", 11, 13, 1, 11, 13, 1.0, "Healer",
             [ab("cr_dg1_sap_mend", 8, "allyHpBelow:60"), ab("cr_dg1_sticky_sap", 5, "targetNoAura:cr_dg1_sticky_sap", 50), ab("cr_dg1_amber_spit", 3)],
             loot="lt_dg1_rootling", portrait="cr_mossling_shaman", hm=0.85, ranged=True, rng=25, school="Nature", resource="Mana",
             material="wood", voice="wood", passives=["cr_dg1_ash_touched"], faction="Kusu's Roots"),
    creature("cr_dg1_root_wisp", "Root-Wisp", "A lantern-flame that used to run through Kusu's roots like sap. The grey seed has drunk it nearly dry.",
             "cr_hollow_wisp", "Spirit", "Normal", 11, 13, 1, 11, 13, 0.9, "Caster",
             [ab("cr_dg1_gutter", 5, "targetNoAura:cr_dg1_guttered", 60), ab("cr_dg1_ashen_bolt", 3)], loot="lt_dg1_wisp",
             hm=0.8, ranged=True, rng=30, school="Shadow", resource="Mana", move=9, material="ether", voice="spirit",
             passives=["cr_dg1_ash_touched"], faction="Kusu's Roots"),
    creature("cr_dg1_greyed_kodama", "Greyed Kodama", "A little tree spirit who used to tend the roots. It has forgotten its own name, and yours.",
             "npc_spirit", "Spirit", "Normal", 11, 13, 1, 11, 13, 1.1, "Healer",
             [ab("cr_dg1_sap_mend", 8, "allyHpBelow:55"), ab("cr_dg1_root_bind", 6, "targetNoAura:cr_dg1_root_bound", 50), ab("cr_dg1_amber_spit", 3)],
             loot="lt_dg1_wisp", portrait="portrait_spirit", hm=0.9, ranged=True, rng=25, school="Nature", resource="Mana",
             material="wood", voice="spirit", passives=["cr_dg1_ash_touched"], faction="Kusu's Roots"),
    creature("cr_dg1_rootling_brute", "Rootling Brute", "A rootling that has eaten a great deal of grey sap and grown to the size of a wheelbarrow. It is not happy about it.",
             "cr_rootling", "Elemental", "Elite", 12, 14, 2, 12, 14, 1.45, "Melee",
             [ab("cr_dg1_burrow_stomp", 7, "enemiesInRange:2"), ab("cr_dg1_thick_bark", 4, "selfHpBelow:70"), ab("cr_dg1_root_headbutt", 5)],
             loot="lt_dg1_elite", portrait="cr_mossling", hm=1.0, dm=0.95, am=1.15, attack=2.6, move=7, material="wood", voice="wood",
             passives=["cr_dg1_ash_touched"], immune=["Fear"], faction="Kusu's Roots",
             bark="*The brute rootling lowers its knotted head and paws the earth like a very small, very wooden bull.*"),
    creature("cr_dg1_root_sentinel", "Rootbound Sentinel", "Two of Kusu's oldest roots, raised long ago to guard the Root Lantern. They still guard it. They no longer remember from whom.",
             "cr_hollow_treant", "Elemental", "Elite", 12, 14, 2, 12, 14, 2.3, "Melee",
             [ab("cr_dg1_root_bind", 6, "targetNoAura:cr_dg1_root_bound"), ab("cr_dg1_ward_of_roots", 4, "allyHpBelow:60"), ab("cr_dg1_sentinel_slam", 5)],
             loot="lt_dg1_elite", portrait="cr_hollow_treant", hm=1.05, dm=1.0, am=1.25, attack=3.0, move=6, material="wood", voice="wood",
             passives=["cr_dg1_ash_touched"], immune=["Fear", "Polymorph", "Sleep"], faction="Kusu's Roots",
             bark="*The two great roots creak upright and turn their mossy faces towards you.*"),
    creature("cr_dg1_rootwarden", "The Rootwarden", "Old Kusu's guardian: a hunched giant of root and moss with a lantern for a heart. A grey seed is lodged inside the lantern, and every heartbeat spreads it a little further.",
             "cr_dg1_rootwarden", "Spirit", "Boss", 13, 14, 2, 12, 14, 3.4, "Boss",
             [ab("cr_dg1_lantern_blaze", 10, "selfHpBelow:30"), ab("cr_dg1_call_sprouts", 10, "selfHpBelow:60"), ab("cr_dg1_ash_bloom", 9),
              ab("cr_dg1_sweeping_claws", 7, "enemiesInRange:2"), ab("cr_dg1_strangling_roots", 6, "targetNoAura:cr_dg1_strangled"), ab("cr_dg1_root_slam", 5)],
             loot="lt_dg1_rootwarden", portrait="cr_hollow_treant", hm=1.7, dm=1.45, am=1.1, attack=2.8, move=7, material="wood", voice="wood",
             passives=["cr_dg1_ash_touched"], immune=["Fear", "Polymorph", "Sleep", "Incapacitate", "Confuse", "Banish"],
             stats=[{"stat": "Resistance", "value": 15, "school": "Nature"}], xpMult=1.3, faction="Kusu's Roots",
             bark="*The Rootwarden lifts its masked head. Inside its cage of ribs the lantern heart flickers honey-gold, then grey, then gold again, as if it is trying to remember you.*"),
    creature("cr_dg1_sprout", "Kusu Sprout", "A sprout shaken loose from the roof. Barely awake, and cross about it.",
             "cr_rootling", "Elemental", "Minion", 11, 14, 0, 11, 14, 0.6, "Melee", [ab("cr_dg1_root_headbutt", 3)],
             portrait="cr_mossling", xpMult=0, material="wood", voice="wood", faction="Kusu's Roots"),
]

# ---------------------------------------------------------------- the northern band's creatures
ABILITIES_LV2 = [
    {"id": "cr_lv2_cleaver_chop", "name": "Cleaver Chop", "icon": "axe", "description": "A wild overhand chop with a notched cleaver.",
     "school": "Physical", "hidden": True, "cooldown": 6, "target": "Enemy", "melee": True,
     "effects": [{"type": "WeaponDamage", "weaponPct": 115}], "aiHint": "Damage", "aiPriority": 4},
    {"id": "cr_lv2_barbed_arrow", "name": "Barbed Arrow", "icon": "arrow", "description": "A barbed arrow that makes the target bleed.",
     "school": "Physical", "hidden": True, "cooldown": 12, "target": "Enemy", "range": 30,
     "effects": [{"type": "WeaponDamage", "weaponPct": 80, "ranged": True}, {"type": "ApplyAura", "aura": "cr_lv2_barbed"}], "aiHint": "Damage", "aiPriority": 4},
    {"id": "cr_lv2_yip", "name": "Pack Yip", "icon": "shout", "description": "A cackling yip that spurs nearby gnolls on. Damage dealt increased by 10% for 12 sec.",
     "school": "Physical", "hidden": True, "cooldown": 30, "target": "Self",
     "area": {"shape": "Circle", "radius": 12, "centeredOnCaster": True, "affects": "Allies"},
     "effects": [{"type": "ApplyAura", "aura": "cr_lv2_yipped"}], "aiHint": "Buff", "aiPriority": 5},
]
AURAS_LV2 = [
    {"id": "cr_lv2_barbed", "name": "Barbed", "icon": "blood", "kind": "Debuff", "duration": 12, "tickInterval": 3, "tags": ["Bleed"],
     "description": "Bleeding.", "tickEffects": [{"type": "Damage", "min": 3, "perLevel": 0.4}]},
    {"id": "cr_lv2_yipped", "name": "Pack Yip", "icon": "shout", "kind": "Buff", "duration": 12,
     "description": "Damage dealt increased by 10%.", "mods": [{"stat": "DamageDone", "value": 10, "pct": True}]},
]
CREATURES_LV2 = [
    creature("cr_lv2_orchard_rootling", "Wandering Rootling", "A little rootling up from under Old Kusu, with an apple under each arm and a guilty look.",
             "cr_rootling", "Elemental", "Normal", 9, 12, -1, 9, 12, 0.8, "Melee", [ab("cr_dg1_root_headbutt", 4)], loot="lt_lv2_rootling",
             portrait="cr_mossling", hm=0.9, material="wood", voice="wood", faction="Kusu's Roots",
             bark="*The rootling clutches its apple and creaks defensively.*"),
    creature("cr_lv2_rootling_sapling", "Rootling Sapling", "A rootling with a glowing bud. It patches up its friends with sap, and its friends patch up the apples.",
             "cr_rootling", "Elemental", "Normal", 9, 12, -1, 9, 12, 0.95, "Healer",
             [ab("cr_dg1_sap_mend", 8, "allyHpBelow:55"), ab("cr_dg1_amber_spit", 3)], loot="lt_lv2_rootling", portrait="cr_mossling_shaman",
             hm=0.85, ranged=True, rng=25, school="Nature", resource="Mana", material="wood", voice="wood", faction="Kusu's Roots"),
    creature("cr_lv2_duskmane_scout", "Duskmane Scout", "A gnoll of the Duskmane clan, a long way east of Amberfield. Lean, nervous and smelling powerfully of lamp oil.",
             "cr_gnoll", "Humanoid", "Normal", 10, 12, 0, 10, 12, 1.95, "Melee", [ab("cr_lv2_yip", 5, "", 50), ab("cr_lv2_cleaver_chop", 4)],
             loot="lt_lv2_gnoll", portrait="cr_bandit", attack=2.4, material="leather", voice="gnoll", faction="Duskmane",
             bark="*The gnoll's ears go flat and it shows a great many teeth.*"),
    creature("cr_lv2_duskmane_lookout", "Duskmane Lookout", "A gnoll archer with a green bandana and a bow it is very proud of.",
             "cr_gnoll_archer", "Humanoid", "Normal", 10, 12, 0, 10, 12, 1.95, "Ranged", [ab("cr_lv2_barbed_arrow", 4)], loot="lt_lv2_gnoll",
             portrait="cr_bandit_archer", hm=0.9, ranged=True, rng=30, material="leather", voice="gnoll", faction="Duskmane"),
    creature("cr_lv2_wheat_tusker", "Wheatfield Tusker", "A boar that has discovered Ama Hollyhock's wheat and is not prepared to un-discover it.",
             "cr_boar", "Beast", "Normal", 9, 12, -1, 9, 12, 1.15, "Melee", [ab("cr_boar_charge", 6)], loot="lt_boar", family="Boar",
             hm=1.05, material="fur", voice="beast", tameable=True, faction="Beasts"),
    creature("cr_lv2_bristlesow", "Old Bristlesow", "The tuskers' mother: broad as a cart, grizzled, and absolutely certain the wheat is hers.",
             "cr_boar", "Beast", "Normal", 10, 12, 0, 10, 12, 1.45, "Melee", [ab("cr_boar_charge", 6)], loot="lt_boar", family="Boar",
             hm=1.4, dm=1.1, attack=2.4, move=7, material="fur", voice="beast", tameable=True, faction="Beasts",
             bark="*Old Bristlesow snorts, lowers her tusks and scrapes the furrow with one hoof.*"),
]


# ====================================================================== items
def items_dg1():
    it = []
    # boss Rares (the guaranteed pool) — item level 18, required 13
    it.append(gear("dg1_lanternheart_hauberk", "Lanternheart Hauberk", "armor",
                   "Root-ribbed mail with a little amber window over the heart. Something inside it still glows when you are brave.",
                   "Rare", 18, 13, "Chest", [("Stamina", 4), ("Strength", 3), ("Defense", 2)], armor_type="Mail", unique=True))
    it.append(gear("dg1_sapwood_staff", "Kusu Sapwood Staff", "staff",
                   "Cut from a root that offered itself. The sap in the grain still rises at dawn and sinks at dusk.",
                   "Rare", 18, 13, "TwoHand", [("Intellect", 4), ("Spirit", 3), ("Stamina", 2), ("HealingPower", 4)], weapon="Staff", unique=True))
    it.append(gear("dg1_rootclaw_grips", "Rootclaw Grips", "hand",
                   "Leather gloves stitched with a dozen tiny root-claws. Excellent for climbing, terrible for knitting.",
                   "Rare", 18, 13, "Hands", [("Agility", 4), ("Stamina", 2), ("AttackPower", 4)], armor_type="Leather", unique=True))
    it.append(gear("dg1_shrine_mask", "Rootwarden's Shrine Mask", "mask",
                   "A pale mask with vermilion marks, small enough for a person. The Rootwarden had a spare. Of course it did.",
                   "Rare", 18, 13, "Head", [("Intellect", 4), ("Stamina", 3), ("SpellDamage", 4)], armor_type="Cloth", unique=True))
    # boss Epics (the chance pool) — item level 22, required 13
    it.append(gear("dg1_ep_seedheart_pendant", "Seedheart Pendant", "heart",
                   "A drop of amber with a single green leaf inside it. Hotaru says it is the first thing Kusu ever grew.",
                   "Epic", 22, 13, "Neck", [("Stamina", 3), ("Spirit", 3), ("Intellect", 3), ("HealingPower", 5)], unique=True))
    it.append(gear("dg1_ep_rootbreaker", "Rootbreaker", "hammer",
                   "A maul of petrified root with an amber lantern set in the head. It hums, low and contented, every time it lands.",
                   "Epic", 22, 13, "TwoHand", [("Strength", 5), ("Stamina", 4), ("MeleeCrit", 1)], weapon="TwoHandMace", unique=True))
    # named greens (trash and chest pools) — item level 16, required 11
    it.append(gear("dg1_mossbark_girdle", "Mossbark Girdle", "armor", "Mail rings threaded through a belt of living moss. It needs watering.",
                   "Uncommon", 16, 11, "Waist", [("Strength", 2), ("Stamina", 2)], armor_type="Mail"))
    it.append(gear("dg1_glowcap_wraps", "Glowcap Wraps", "armor", "Cloth wraps dyed with glowing mushroom juice. You can read by them, slightly.",
                   "Uncommon", 16, 11, "Wrist", [("Intellect", 2), ("Spirit", 1)], armor_type="Cloth"))
    it.append(gear("dg1_rootrunner_boots", "Rootrunner Boots", "boot", "Soft boots for running along roots in the dark without waking anything.",
                   "Uncommon", 16, 11, "Feet", [("Agility", 2), ("Stamina", 2)], armor_type="Leather"))
    it.append(gear("dg1_sapstained_cloak", "Sap-Stained Cloak", "wings", "It sticks to things. Mostly to you.",
                   "Uncommon", 16, 11, "Back", [("Stamina", 2), ("Agility", 1)], armor_type="Cloth"))
    it.append(gear("dg1_amber_band", "Amber Band", "halo", "A ring of root-amber with a tiny bubble of very old air inside.",
                   "Uncommon", 16, 11, "Finger", [("Intellect", 2), ("Stamina", 1)]))
    # quest rewards (The Heart Under the Roots) — Rare, item level 17, required 12
    it.append(gear("dg1_hotarus_lantern_charm", "Hotaru's Lantern Charm", "fire", "A paper lantern no bigger than a plum. It glows a little brighter when you are lost.",
                   "Rare", 17, 12, "Trinket", [("Spirit", 3), ("Stamina", 2), ("HealingPower", 3)], unique=True))
    it.append(gear("dg1_heartwood_legguards", "Heartwood Legguards", "armor", "Mail legguards lined with a sliver of the Rootwarden's own heartwood, given willingly.",
                   "Rare", 17, 12, "Legs", [("Stamina", 4), ("Strength", 3), ("Agility", 2)], armor_type="Mail", unique=True))
    it.append(gear("dg1_rootsong_wand", "Rootsong Wand", "staff", "A crooked root twig. When you swing it, the faintest note sounds — the right one, this time.",
                   "Rare", 17, 12, "Ranged", [("Intellect", 2), ("SpellDamage", 2)], weapon="Wand", unique=True))
    # quest and junk items
    it.append({"id": "dg1_ash_seed", "name": "Ash-Seed", "icon": "void",
               "description": "A seed the size of a plum, grey as ash and cold as a cellar step. It weighs far more than it should. Something about it smells of a fire very far away.",
               "kind": "Quest", "quality": "Common", "itemLevel": 1, "stack": 1, "price": 0, "unique": True, "quest": "dg1_heart_of_kusu"})
    it.append({"id": "dg1_amber_sap", "name": "Lump of Amber Sap", "icon": "water_drop", "kind": "Junk", "quality": "Poor", "itemLevel": 1, "stack": 10, "price": 210})
    it.append({"id": "dg1_grey_root_knot", "name": "Grey Root Knot", "icon": "leaf", "kind": "Junk", "quality": "Poor", "itemLevel": 1, "stack": 10, "price": 260})
    return it


def items_lv2():
    it = []
    it.append(gear("lv2_pippin_gloves", "Pippin Gloves", "hand", "Soft leather picking gloves, stained cider-brown at the fingertips.",
                   "Uncommon", 14, 9, "Hands", [("Agility", 2), ("Stamina", 1)], armor_type="Leather"))
    it.append(gear("lv2_orchard_shawl", "Orchard-Keeper's Shawl", "wings", "Knitted by Hana's grandmother, darned by her mother, and smelling permanently of apples.",
                   "Uncommon", 14, 9, "Back", [("Spirit", 1), ("Intellect", 2)], armor_type="Cloth"))
    it.append(gear("lv2_appleknot_cudgel", "Appleknot Cudgel", "mace", "A knotted applewood cudgel. Hana uses it on wasps, and once, memorably, on a tax collector.",
                   "Uncommon", 14, 9, "OneHand", [("Strength", 1), ("Stamina", 2)], weapon="OneHandMace"))
    it.append(gear("lv2_moppets_spare_bell", "Moppet's Spare Bell", "sparkle", "A tiny brass bell on a green ribbon. Ring it where it's quiet, and somebody always comes.",
                   "Uncommon", 13, 9, "Neck", [("Spirit", 2), ("Stamina", 1)]))
    it.append(gear("lv2_lamplighters_crook", "Lamplighter's Crook", "staff", "A long ash pole with a brass hook for lifting lantern lids. In a pinch it lifts other things too.",
                   "Uncommon", 15, 10, "TwoHand", [("Intellect", 3), ("Spirit", 3), ("Stamina", 2)], weapon="Staff"))
    it.append(gear("lv2_oilskin_jerkin", "Oilskin Jerkin", "armor", "Waxed leather that sheds rain, lamp oil and opinions.",
                   "Uncommon", 15, 10, "Chest", [("Agility", 3), ("Stamina", 3), ("Strength", 2)], armor_type="Leather"))
    it.append(gear("lv2_wickbraid_ring", "Wickbraid Ring", "halo", "A ring of braided spirit-wick, blessed at the shrine. It is very slightly warm.",
                   "Uncommon", 15, 10, "Finger", [("Intellect", 2), ("Stamina", 1)]))
    it.append({"id": "lv2_bitten_apple", "name": "Bitten Apple", "icon": "food", "description": "One neat bite out of it. Only one.",
               "kind": "Junk", "quality": "Poor", "itemLevel": 1, "stack": 20, "price": 35})
    it.append({"id": "lv2_gnoll_fang", "name": "Duskmane Fang", "icon": "fang", "kind": "Junk", "quality": "Poor", "itemLevel": 1, "stack": 10, "price": 180})
    return it


# ====================================================================== loot
LOOT_DG1 = [
    {"id": "lt_dg1_rootling", "goldMin": 60, "goldMax": 140, "entries": [
        {"item": "dg1_amber_sap", "chance": 35}, {"item": "dg1_grey_root_knot", "chance": 25}, {"item": "potion_healing", "chance": 5},
        {"pool": ["dg1_mossbark_girdle", "dg1_glowcap_wraps", "dg1_rootrunner_boots", "dg1_sapstained_cloak", "dg1_amber_band"], "chance": 3},
        {"random": True, "chance": 6, "quality": "Uncommon"}]},
    {"id": "lt_dg1_wisp", "goldMin": 60, "goldMax": 140, "entries": [
        {"item": "junk_grey_ash", "chance": 40}, {"item": "potion_lesser_mana", "chance": 6},
        {"pool": ["dg1_mossbark_girdle", "dg1_glowcap_wraps", "dg1_rootrunner_boots", "dg1_sapstained_cloak", "dg1_amber_band"], "chance": 3},
        {"random": True, "chance": 6, "quality": "Uncommon"}]},
    {"id": "lt_dg1_elite", "goldMin": 220, "goldMax": 420, "entries": [
        {"item": "dg1_grey_root_knot", "chance": 50},
        {"pool": ["dg1_mossbark_girdle", "dg1_glowcap_wraps", "dg1_rootrunner_boots", "dg1_sapstained_cloak", "dg1_amber_band"], "chance": 22, "partyUsable": True},
        {"random": True, "chance": 35, "quality": "Uncommon", "itemLevelOffset": 1}, {"random": True, "chance": 6, "quality": "Rare"}]},
    {"id": "lt_dg1_rootwarden", "goldMin": 1800, "goldMax": 2800, "entries": [
        {"item": "dg1_ash_seed"},
        {"pool": ["dg1_lanternheart_hauberk", "dg1_sapwood_staff", "dg1_rootclaw_grips", "dg1_shrine_mask"], "partyUsable": True, "skipOwned": True},
        {"pool": ["dg1_ep_seedheart_pendant", "dg1_ep_rootbreaker"], "chance": 15, "partyUsable": True, "skipOwned": True},
        {"random": True, "chance": 100, "quality": "Uncommon", "itemLevelOffset": 1}]},
    {"id": "lt_dg1_chest_cache", "goldMin": 300, "goldMax": 650, "entries": [
        {"item": "potion_healing", "chance": 60}, {"item": "potion_lesser_mana", "chance": 40}, {"item": "elixir_wisdom", "chance": 20},
        {"pool": ["dg1_mossbark_girdle", "dg1_glowcap_wraps", "dg1_rootrunner_boots", "dg1_sapstained_cloak", "dg1_amber_band"], "partyUsable": True, "skipOwned": True},
        {"random": True, "chance": 20, "quality": "Rare"}]},
    {"id": "lt_dg1_chest_hoard", "goldMin": 600, "goldMax": 1100, "entries": [
        {"item": "dg1_amber_sap", "min": 2, "max": 4},
        {"random": True, "chance": 100, "quality": "Uncommon", "itemLevelOffset": 1}, {"random": True, "chance": 40, "quality": "Rare"}]},
    {"id": "lt_dg1_chest_lantern", "goldMin": 800, "goldMax": 1400, "entries": [
        {"item": "potion_healing", "min": 2, "max": 3},
        {"pool": ["dg1_mossbark_girdle", "dg1_glowcap_wraps", "dg1_rootrunner_boots", "dg1_sapstained_cloak", "dg1_amber_band"], "partyUsable": True, "skipOwned": True},
        {"random": True, "chance": 100, "quality": "Rare"}]},
]
LOOT_LV2 = [
    {"id": "lt_lv2_rootling", "goldMin": 25, "goldMax": 80, "entries": [
        {"item": "lv2_bitten_apple", "chance": 60}, {"item": "dg1_amber_sap", "chance": 15}, {"random": True, "chance": 5, "quality": "Uncommon"}]},
    {"id": "lt_lv2_gnoll", "goldMin": 60, "goldMax": 150, "entries": [
        {"item": "lv2_gnoll_fang", "chance": 45}, {"item": "potion_lesser_healing", "chance": 8}, {"random": True, "chance": 7, "quality": "Uncommon"}]},
    {"id": "lt_lv2_hill_basket", "goldMin": 150, "goldMax": 300, "entries": [
        {"item": "potion_healing", "chance": 60}, {"item": "elixir_wisdom", "chance": 25}, {"random": True, "chance": 100, "quality": "Uncommon"}]},
    {"id": "lt_lv2_root_hollow", "goldMin": 250, "goldMax": 500, "entries": [
        {"item": "dg1_amber_sap", "min": 1, "max": 3}, {"random": True, "chance": 100, "quality": "Uncommon"}, {"random": True, "chance": 15, "quality": "Rare"}]},
]


# ====================================================================== NPCs
NPCS = [
    {"id": "lv2_hana_pipp", "name": "Hana Pipp", "title": "<Orchard-keeper>", "sprite": "npc_villager_a", "portrait": "portrait_villager_a",
     "dialogue": "dlg_lv2_hana_pipp", "bark": "One bite. ONE bite. Out of every single apple!", "shortName": "Hana"},
    {"id": "lv2_teo", "name": "Teo Marsh", "title": "<Retired Lamplighter>", "sprite": "npc_villager_b", "portrait": "portrait_villager_b",
     "dialogue": "dlg_lv2_teo", "bark": "Shh. The big one's listening.", "shortName": "Teo"},
    {"id": "lv2_ama_hollyhock", "name": "Ama Hollyhock", "title": "<Beekeeper>", "sprite": "npc_villager_a", "portrait": "portrait_villager_a",
     "dialogue": "dlg_lv2_ama_hollyhock", "bark": "Mind the bees, dear. They don't mind you.", "shortName": "Ama"},
    {"id": "lv2_sprout", "name": "Sprout", "title": "<Orchard Rootling>", "sprite": "cr_rootling", "portrait": "cr_mossling",
     "dialogue": "dlg_lv2_sprout", "bark": "*Creak.*"},
    {"id": "lv2_snaggle", "name": "Snaggle", "title": "<Duskmane Scout>", "sprite": "cr_gnoll", "portrait": "cr_bandit"},
]
NPCS_DG1 = [
    {"id": "dg1_hotaru", "name": "Hotaru", "title": "<Root-Lantern Spirit>", "sprite": "npc_spirit", "portrait": "portrait_spirit",
     "dialogue": "dlg_dg1_hotaru", "bark": "*A tiny paper-lantern spirit bobs among the mushrooms, glowing honey-gold.*", "shortName": "Hotaru"},
]


# ====================================================================== quests
QUESTS_LV2 = [
    {"id": "lv2_orchard_thieves", "name": "Bitten, Every One", "giver": "lv2_hana_pipp", "level": 10, "minLevel": 9, "zone": "lanternvale",
     "summary": "Something creeps into the Pipp orchard at night and takes exactly one bite out of every apple. Hana Pipp would very much like to know what — and then to have a word with it.",
     "stages": [
         {"id": "look", "description": "Look over the tipped apple crate in the middle of the Pipp orchard, north of the meadow.",
          "objectives": [{"type": "Flag", "target": "lv2_orchard_tracks", "text": "Search the spilled apple crate"}], "next": "rootlings"},
         {"id": "rootlings", "description": "Catch the orchard thieves in the north rows.",
          "objectives": [{"type": "Defeat", "target": "enc_lv2_orchard_rootlings", "text": "Deal with the orchard thieves"}], "next": "report"},
         {"id": "report", "description": "Tell Hana Pipp who has been eating her apples.",
          "objectives": [{"type": "Flag", "target": "lv2_orchard_reported", "text": "Report to Hana Pipp"}], "turnIn": "lv2_hana_pipp"},
     ],
     "rewards": {"xp": 400, "gold": 450, "choiceItems": ["lv2_pippin_gloves", "lv2_orchard_shawl", "lv2_appleknot_cudgel"]}},
    {"id": "lv2_singing_roots", "name": "The Singing Roots", "giver": "child_nell", "level": 10, "minLevel": 10, "zone": "lanternvale",
     "summary": "Moppet says the roots behind Old Kusu are singing. Not happy singing — the kind you do when something hurts and you don't want anyone to know. Grown-ups can't hear it. Nell thinks you might.",
     "stages": [
         {"id": "listen", "description": "Listen at the great root behind Old Kusu, north of the village square.",
          "objectives": [{"type": "Flag", "target": "lv2_roots_listened", "text": "Listen to the singing roots"}], "next": "tell"},
         {"id": "tell", "description": "Tell Nell what the roots are singing about.",
          "objectives": [{"type": "Flag", "target": "lv2_roots_told", "text": "Tell Nell"}], "turnIn": "child_nell"},
     ],
     "rewards": {"xp": 300, "gold": 200, "items": ["lv2_moppets_spare_bell"]}},
    {"id": "lv2_lantern_oil", "name": "Oil for Walk Night", "giver": "lamplighter_tobben", "level": 11, "minLevel": 10, "zone": "lanternvale",
     "summary": "Three casks of lamp oil have vanished from Tobben's store on the west road, the week before Walk Night. Every lantern in the valley wants filling, and Tobben refuses to light them with good intentions.",
     "stages": [
         {"id": "tracks", "description": "Look for the casks' trail on the west road, north-west of Old Kusu.",
          "objectives": [{"type": "Reach", "target": "reg_lv2_oil_tracks", "text": "Find the trail on the west road"}], "next": "scouts"},
         {"id": "scouts", "description": "Get the oil back from whoever took it. The trail leads to the old waystation north of the road.",
          "objectives": [{"type": "Flag", "target": "lv2_scouts_dealt", "text": "Recover the lamp oil"}], "next": "return"},
         {"id": "return", "description": "Tell Tobben what became of his oil.",
          "objectives": [{"type": "Flag", "target": "lv2_oil_returned", "text": "Return to Tobben"}], "turnIn": "lamplighter_tobben"},
     ],
     "rewards": {"xp": 450, "gold": 500, "choiceItems": ["lv2_lamplighters_crook", "lv2_oilskin_jerkin", "lv2_wickbraid_ring"]}},
]
QUESTS_DG1 = [
    {"id": "dg1_heart_of_kusu", "name": "The Heart Under the Roots", "giver": "dg1_hotaru", "level": 12, "minLevel": 11, "zone": "dgn_root_hollows",
     "summary": "Hotaru, the last root-lantern spirit, says a grey seed fell through the soil to Old Kusu's roots. The Rootwarden swallowed it into his lantern heart to keep it from the tree — and it has been hollowing him out ever since.",
     "stages": [
         {"id": "warden", "description": "Free the Rootwarden in the deepest hollow, past the Sentinel Gate.",
          "objectives": [{"type": "Defeat", "target": "enc_dg1_rootwarden", "text": "Free the Rootwarden"}], "next": "hotaru"},
         {"id": "hotaru", "description": "Bring the Ash-Seed back to Hotaru in the Root Grove.",
          "objectives": [{"type": "Flag", "target": "dg1_hotaru_told", "text": "Return to Hotaru"}], "next": "elder", "turnIn": "dg1_hotaru"},
         {"id": "elder", "description": "Show the Ash-Seed to Elder Maru under Old Kusu in Lanternvale.",
          "objectives": [{"type": "Flag", "target": "dg1_seed_shown", "text": "Show the seed to Elder Maru"}], "turnIn": "elder_maru"},
     ],
     "rewards": {"xp": 450, "gold": 1500, "choiceItems": ["dg1_hotarus_lantern_charm", "dg1_heartwood_legguards", "dg1_rootsong_wand"]}},
]


# ====================================================================== dialogues
def dlg_hana():
    q = "lv2_orchard_thieves"
    return {"id": "dlg_lv2_hana_pipp", "start": "h_done_friend", "nodes": [
        N("h_done_friend", "lv2_hana_pipp", "Look — the rootling row! I leave them the windfalls and a lantern on the fence, and they chase the wasps off and sing to the blossom. Best harvest in ten years. Don't tell the other orchards.",
          [C("How is Sprout?", "h_sprout"), C("Goodbye.")], [qdone(q), flag("lv2_rootling_friend")], "h_done"),
        N("h_sprout", "lv2_hana_pipp", "Sprout? He sleeps in the crate you found, under a sack. He's taken one bite out of my hat. I'm choosing to find it charming.", [C("Goodbye.")]),
        N("h_done", "lv2_hana_pipp", "Not one bitten apple since. I've started counting them again for fun. Four thousand and twelve. Four thousand and eleven — I just ate one.",
          [C("Goodbye.")], [qdone(q)], "h_report"),
        N("h_report", "lv2_hana_pipp", "Well? Did you catch them? Who's been eating my apples?",
          [C("Rootlings — little sprouts of Old Kusu, up from under the roots. They were lost and hungry, not wicked.", "h_rep_kind"),
           C("Some root-creatures. They won't trouble you again.", "h_rep_plain")], [qstate(q, "report")], "h_active"),
        N("h_rep_kind", "lv2_hana_pipp", "Sprouts of Old Kusu? Walking about? *She looks at the bitten apples in a new light.* ...Well, they've got the manners of saplings. What do I do — chase them off with a broom every night?",
          [C("They come up looking for light. Leave them the windfalls and a lantern, and they'll guard your trees from worse things.",
             check=chk("Nature", 12, "h_rep_friend", "h_rep_doubt"), once=True, tag="NATURE"),
           C("The earth spirits ask you to share with them. They'll pay you back in blossom.", "h_rep_friend", conds=[cls("Shaman")], tag="SHAMAN"),
           C("Every lantern is for everyone, Hana. Even the little wooden ones.", "h_rep_friend", conds=[cls("Priest")], tag="PRIEST"),
           C("Keep your crates covered, and they'll find somewhere else.", "h_rep_plain")]),
        N("h_rep_doubt", "lv2_hana_pipp", "Guard my trees? Those little thieves? *She narrows her eyes at you, then at the orchard.* I'll keep the crates covered, thank you.", next="h_rep_plain"),
        N("h_rep_friend", "lv2_hana_pipp", "A row for the rootlings... *She laughs.* My granny used to leave a bowl out for 'the little folk'. I thought she meant hedgehogs. All right. One row, one lantern, all the windfalls they can carry.",
          outcomes=[setf("lv2_rootling_friend"), setf("lv2_orchard_reported"), done(q), give("lv2_bitten_apple", 3)], next="h_thanks"),
        N("h_rep_plain", "lv2_hana_pipp", "Gone, then. Good. *She sounds relieved, and a tiny bit sorry.* Here — for your trouble, and a bag of the ones they didn't get to.",
          outcomes=[setf("lv2_orchard_reported"), done(q), gold(250)], next="h_thanks"),
        N("h_thanks", "lv2_hana_pipp", "And take your pick from the shed. Grandad's things — he'd want them used, not dusted.", [C("Thank you, Hana. Goodbye.")]),
        N("h_active", "lv2_hana_pipp", "The crate's in the middle of the orchard, by the third row. Look for the apples with one bite out. They are all of them.",
          [C("Goodbye.")], [qactive(q)], "h_greet"),
        N("h_greet", "lv2_hana_pipp", "Mind the windfalls! Hana Pipp — the orchard's mine. Well. Mine, the bees', and lately SOMEBODY ELSE'S.",
          [C("Somebody else's?", "h_offer", conds=[level(9), qnot(q)]),
           C("Tell me about the orchard.", "h_lore"),
           C("Goodbye.")]),
        N("h_lore", "lv2_hana_pipp", "Forty-eight trees, planted by my great-great-grandad the year the village got its first lantern. Old Kusu's roots run right under them — that's why the apples taste of honey. Everybody says so. I say so loudest.",
          [C("Somebody else's apples?", "h_offer", conds=[level(9), qnot(q)]), C("Goodbye.")]),
        N("h_offer", "lv2_hana_pipp", "Every night something gets in and takes ONE bite out of every apple. One! It isn't even greedy, it's just rude. They had a party round the crate in the middle row last night — have a look, would you? I'd go, but I'd lose my temper, and then my broom.",
          [C("I'll find your thief.", "h_accept", conds=[level(9)], outs=[start(q)]), C("Not right now, Hana.")]),
        N("h_accept", "lv2_hana_pipp", "Bless you. If it's the Fernsby children, bring them back by the ear. If it's anything else, use your judgement. I trust your judgement. I don't trust the Fernsby children.",
          [C("Goodbye.")]),
    ]}


def dlg_crate():
    q = "lv2_orchard_thieves"
    return {"id": "dlg_lv2_spilled_crate", "start": "c_start", "nodes": [
        N("c_start", "narrator", "The apple crate lies on its side. Every apple has exactly one neat bite out of it, and the soft earth around it is stippled with tiny prints — not paws, not boots. More like the ends of roots.",
          [C("Follow the prints.", check=chk("Survival", 11, "c_surv_s", "c_surv_f"), once=True, tag="SURVIVAL"),
           C("Take a closer look at those 'footprints'.", check=chk("Nature", 12, "c_nat_s", "c_nat_f"), once=True, tag="NATURE"),
           C("Wait among the trees and see who comes back for seconds.", "c_found")], [qstate(q, "look")], "c_plain"),
        N("c_surv_s", "narrator", "The prints wander from tree to tree, one bite per apple, and they are fresh: a few minutes old. Whoever it is, they're still in the north rows.", outcomes=[xp(60)], next="c_found"),
        N("c_surv_f", "narrator", "The prints go round the crate, round the next tree, round you... You stop before you get dizzy.", next="c_found"),
        N("c_nat_s", "narrator", "Those aren't feet — they're root-tips, the kind a sapling puts down. Something rooty has walked up here from under Old Kusu. It has also, clearly, been enjoying itself.",
          outcomes=[xp(60), setf("lv2_knows_rootlings")], next="c_found"),
        N("c_nat_f", "narrator", "Squirrels, you decide firmly. Very heavy squirrels. With roots.", next="c_found"),
        N("c_found", "narrator", "A rustle in the north rows. Something small, pale and root-legged trundles between the trees with an apple in its arms — and it is not alone.",
          [C("(Go after them.)")], outcomes=[setf("lv2_orchard_tracks"), setf("lv2_orchard_hunt")]),
        N("c_plain", "narrator", "A tipped apple crate. Every apple in it has one neat bite out of it. Somebody has very good manners, or very small mouths.", [C("(Step back.)")]),
    ]}


def dlg_listening_root():
    q = "lv2_singing_roots"
    reveal = [setf("lv2_roots_listened"), setf("found_root_hollows")]
    return {"id": "dlg_lv2_listening_root", "start": "r_found", "nodes": [
        N("r_found", "narrator", "The great root is warm under your hand, and the hum is louder now that you know where it comes from: the mossy mouth between the two biggest roots, breathing cool air up from below.",
          [C("(Stand up.)")], [flag("found_root_hollows")], "r_start", outcomes=[setf("lv2_roots_listened")]),
        N("r_start", "narrator", "You kneel and press your ear to the great root. It is warm. Under the bark something hums — one long, low note, wavering like a voice trying very hard not to cry.",
          [C("Listen for where the note comes from.", check=chk("Perception", 13, "r_perc_s", "r_perc_f"), once=True, tag="PERCEPTION"),
           C("Read the root itself: the bark, the sap, the way it runs.", check=chk("Nature", 12, "r_nat_s", "r_nat_f"), once=True, tag="NATURE"),
           C("Say a quiet prayer with the old tree.", check=chk("Religion", 12, "r_rel_s", "r_rel_f"), once=True, tag="RELIGION"),
           C("Ask the spirits of the earth what is wrong.", "r_shaman", conds=[cls("Shaman")], tag="SHAMAN"),
           C("Feel for the draught on your face, the way you would in a fox's earth.", "r_hunter", conds=[cls("Hunter")], tag="HUNTER"),
           C("Just listen.", "r_listen")]),
        N("r_perc_s", "narrator", "The note isn't in the root at all. It's coming up through it, from below — from the mound of moss and fern between the two biggest roots, where a cool draught is breathing out of the ground. There is a hollow under Old Kusu. And something in it is singing.",
          [C("(Stand up.)")], outcomes=reveal + [xp(80)]),
        N("r_perc_f", "narrator", "You listen until your ear goes numb. The note goes on and on, from everywhere and nowhere at once.", [C("(Stand up.)")], outcomes=[setf("lv2_roots_listened")]),
        N("r_nat_s", "narrator", "The sap is running the wrong way: down, not up, drawn towards the mound of moss between the two biggest roots as if something under it is drinking. When you push the ferns aside, cold air breathes out of a dark mouth in the earth.",
          [C("(Stand up.)")], outcomes=reveal + [xp(80)]),
        N("r_nat_f", "narrator", "It's a root. A very big, very old, very warm root. Beyond that, the tree keeps its own counsel.", [C("(Stand up.)")], outcomes=[setf("lv2_roots_listened")]),
        N("r_rel_s", "narrator", "In the quiet of the prayer the note becomes a word, or nearly: 'below'. When you open your eyes, the ferns on the mossy mound between the roots are stirring in a breeze that is coming out of the ground.",
          [C("(Stand up.)")], outcomes=reveal + [xp(80)]),
        N("r_rel_f", "narrator", "The tree is old and patient and not, at the moment, talking to you.", [C("(Stand up.)")], outcomes=[setf("lv2_roots_listened")]),
        N("r_shaman", "narrator", "The earth answers slowly, the way old earth does: the roots are hurting, because the one who guards them is hurting. He is below. The way down is under the moss between the two biggest roots.",
          [C("(Stand up.)")], outcomes=reveal + [xp(80)]),
        N("r_hunter", "narrator", "There — on your cheek, from the left. Cool air, steady as breath, coming out of the mound of moss and fern between the two biggest roots. Something below has a way in, and a way out.",
          [C("(Stand up.)")], outcomes=reveal + [xp(80)]),
        N("r_listen", "narrator", "You listen. The note rises a little, as if it is glad someone has noticed, then sinks back into the dark under the tree.", [C("(Stand up.)")], outcomes=[setf("lv2_roots_listened")]),
    ]}


def dlg_scouts():
    enc = "enc_lv2_duskmane_scouts"
    peace = [setf("lv2_scouts_dealt")]
    return {"id": "dlg_lv2_duskmane_scouts", "start": "g_start", "nodes": [
        N("g_start", "lv2_snaggle", "*Three gnolls look up from Tobben's oil casks: two with notched cleavers, one with a bow and a very guilty expression.* Hrrk. Not yours. Found it. Finders-keepers is gnoll law, yes?",
          [C("Those casks belong to Lanternvale. Put them down.", "g_fight"),
           C("Drop the oil and run, or I'll make a rug out of you.", check=chk("Intimidation", 13, "g_scared", "g_angry"), once=True, tag="INTIMIDATION"),
           C("Gnolls don't burn lamp oil. What do you want it for?", check=chk("Insight", 12, "g_why_s", "g_why_f"), once=True, tag="INSIGHT"),
           C("Toss them the smoked boar haunch from your pack.", "g_hunter", conds=[cls("Hunter")], tag="HUNTER"),
           C("Point past them and gasp: 'Is that a BEAR?'", "g_rogue", conds=[cls("Rogue")], tag="ROGUE")]),
        N("g_fight", "lv2_snaggle", "*The scout bares every one of its teeth.* Then come take, small lantern-person!", [C("(Fight.)", outs=[fight(enc)])]),
        N("g_angry", "lv2_snaggle", "*The gnolls cackle — a little too loudly.* Big words! Small lantern-person! Get them!", [C("(Fight.)", outs=[fight(enc)])]),
        N("g_scared", "lv2_snaggle", "*All three gnolls look at you, then at each other, then at the horizon.* ...Rug. No. Not rug. Keep oil. Bad oil. Smelly. *They are gone in a scrabble of claws and a cloud of dust.*",
          [C("(Let them go.)")], outcomes=peace + [setf("lv2_gnolls_scared"), xp(120)]),
        N("g_hunter", "lv2_snaggle", "*The haunch sails over their heads. Three noses follow it. Three gnolls follow their noses. Somewhere over the hill, a fight breaks out over who gets the bone.*",
          [C("(Roll the casks back to the road.)")], outcomes=peace + [setf("lv2_gnolls_scared"), xp(120)]),
        N("g_rogue", "lv2_snaggle", "*Three heads whip round. By the time they turn back, you are sitting on the oil casks, filing your nails. The gnolls decide, as one, that this is witchcraft, and leave.*",
          [C("(Roll the casks back to the road.)")], outcomes=peace + [setf("lv2_gnolls_scared"), xp(120)]),
        N("g_why_f", "lv2_snaggle", "*The scout squints at you.* Why lantern-person ask so many why? Suspicious. VERY suspicious.",
          [C("Those casks belong to Lanternvale. Put them down.", "g_fight"), C("Fine. Have it your way.", "g_fight")]),
        N("g_why_s", "lv2_snaggle", "*The scout's ears droop.* ...Lights in the west going grey. Grey comes in the night, and in the morning the old ones don't wake. Big Chief says fire keeps the grey away. Oil makes fire. We take oil, we stay warm. Yes?",
          [C("Take one cask and go home. The other two stay — the valley needs them for Walk Night.", check=chk("Persuasion", 12, "g_share", "g_share_f"), once=True, tag="PERSUASION"),
           C("Grey or not, it's stolen.", "g_fight")], outcomes=[setf("lv2_knows_grey_west")]),
        N("g_share", "lv2_snaggle", "*Long pause. Much ear-twitching.* ...One. Fair. One for Duskmane, two for lantern-people. *The smallest gnoll hugs its cask like a puppy.* You tell Big Chief, Snaggle was fair. Snaggle was VERY fair.",
          [C("(Let them go.)")], outcomes=peace + [setf("lv2_gnolls_shared"), xp(150)]),
        N("g_share_f", "lv2_snaggle", "*The scout looks at the three casks, then at you, then at the three casks.* No. Three is more than one. Gnoll maths.", [C("(Fight.)", outs=[fight(enc)])]),
    ]}


def dlg_teo():
    return {"id": "dlg_lv2_teo", "start": "t_greet", "nodes": [
        N("t_greet", "lv2_teo", "*An old man in a battered hat doesn't take his eyes off his float.* Evening. Or morning. Whichever it is, keep your shadow off the water. The big one's listening.",
          [C("Caught anything?", "t_fish"), C("What big one?", "t_big"), C("Have you heard the roots behind Old Kusu?", "t_roots"),
           C("You were a lamplighter?", "t_lamp"), C("Goodbye.")]),
        N("t_fish", "lv2_teo", "Three boots, a kettle and Bram's hat. The hat was still on Bram. Long story.", next="t_more"),
        N("t_big", "lv2_teo", "Old Grandfather Carp. Older than me, longer than me, and cleverer than me by a whisker. Forty years I've been trying. I think he comes up to laugh.",
          [C("Let me help you land him.", check=chk("Athletics", 13, "t_carp_s", "t_carp_f"), once=True, tag="ATHLETICS"), C("Good luck with him.", "t_more")]),
        N("t_carp_s", "lv2_teo", "*You take the line just as it goes taut. For one glorious moment a golden back the size of a door breaks the water — then the line goes slack, and a single huge scale floats to the surface.* ...He let us see him. Forty years. Ha! HA! Here — take this, for luck.",
          outcomes=[xp(80), gold(150)], next="t_more"),
        N("t_carp_f", "lv2_teo", "*The line twangs, you sit down very suddenly in the reeds, and something under the water makes a noise exactly like a laugh.* See? Every time.", next="t_more"),
        N("t_roots", "lv2_teo", "Hear them? Ha. I've lit lanterns forty years, and the old lamplighters told me: Kusu's roots carry the light, same as a wick carries oil. When they hum, all's well. Lately they don't hum. They moan. The frogs go quiet when they start.",
          [C("Is there a way down to the roots?", "t_roots2"), C("I see.", "t_more")]),
        N("t_roots2", "lv2_teo", "There used to be a Root Lantern, down under the tree, that the first lamplighters tended. Nobody's been down in my lifetime. If there's a door, it's grown over. You'd want sharp ears to find it — or a child's. Children always know where the doors are.",
          next="t_more"),
        N("t_lamp", "lv2_teo", "Forty years, man and boy. I lit the Walk Night lanterns the year the Warden came down so close I could have touched his antlers. I didn't. You don't. Now Tobben does the ladders and I do the fish.", next="t_more"),
        N("t_more", "lv2_teo", "Anything else? Quietly, mind.",
          [C("What big one?", "t_big"), C("Have you heard the roots behind Old Kusu?", "t_roots"), C("You were a lamplighter?", "t_lamp"), C("Goodbye.")]),
    ]}


def dlg_ama():
    return {"id": "dlg_lv2_ama_hollyhock", "start": "a_greet", "nodes": [
        N("a_greet", "lv2_ama_hollyhock", "Mind the bees, dear — they don't mind you. Ama Hollyhock. That's my farm, my wheat, my bees, and that scarecrow is my late husband's coat, so be respectful.",
          [C("Your bees seem uneasy.", check=chk("Nature", 11, "a_bees_s", "a_bees_f"), once=True, tag="NATURE"),
           C("Is something bothering your wheat?", "a_boars"), C("Are you from round here?", "a_family"), C("Goodbye.")]),
        N("a_bees_s", "lv2_ama_hollyhock", "You've a good eye. They don't like the north wind this year. It smells of smoke, they say, though there's no fire anywhere. Bees know things. I just sell the honey.",
          outcomes=[xp(50)], next="a_more"),
        N("a_bees_f", "lv2_ama_hollyhock", "Restless? Dear, they're bees. They've been restless since the beginning of the world.", next="a_more"),
        N("a_boars", "lv2_ama_hollyhock", "Boars! Old Bristlesow and her great lumps of sons. They've flattened a quarter of the east field already. If you were to... discourage them, I'd bake you something. I bake very well.",
          next="a_more"),
        N("a_family", "lv2_ama_hollyhock", "Born in that very farmhouse. My sister married over the downs in Amberfield — the Haybrights. Her granddaughter Bruna swings a billhook like she's personally offended by hedges. Lovely girl. Frightens the gnolls.",
          next="a_more"),
        N("a_more", "lv2_ama_hollyhock", "Anything else, dear? The honey won't jar itself.",
          [C("Is something bothering your wheat?", "a_boars"), C("Are you from round here?", "a_family"), C("Goodbye.")]),
    ]}


def dlg_sprout():
    return {"id": "dlg_lv2_sprout", "start": "s_start", "nodes": [
        N("s_start", "lv2_sprout", "*The little rootling holds up an apple with one neat bite out of it and offers it to you, very solemnly.*",
          [C("(Take the apple.)", outs=[give("lv2_bitten_apple")], once=True), C("(Pat its leaf.)")]),
    ]}


def dlg_hotaru():
    q = "dg1_heart_of_kusu"
    return {"id": "dlg_dg1_hotaru", "start": "o_done", "nodes": [
        N("o_done", "dg1_hotaru", "Listen — can you hear it? The roots are singing the right note again. The Rootwarden's asleep by the Root Lantern, snoring like a happy hillside. Thank you. Kusu thanks you too. He's just slow about it.",
          [C("Goodbye.")], [qdone(q)], "o_elder"),
        N("o_elder", "dg1_hotaru", "Has your Elder seen the seed yet? Don't keep it near anything that grows. Or anything that sleeps. Or me.",
          [C("I'm on my way.")], [qstate(q, "elder")], "o_return"),
        N("o_return", "dg1_hotaru", "*Hotaru's lantern-head flares bright as noon.* You did it! I can feel him — he's sleeping, properly sleeping, for the first time in a year. And that... that's the seed.",
          [C("Here it is. What should I do with it?", "o_return2", conds=[has("dg1_ash_seed")]),
           C("I'll come back with it.", conds=[{"type": "NotHasItem", "key": "dg1_ash_seed", "amount": 1}])], [qstate(q, "hotaru")], "o_active"),
        N("o_return2", "dg1_hotaru", "Don't give it to ME! *She bobs backwards three feet.* Take it to someone who knows what to do with grey things. Your Elder, under the tree — she's been talking to Kusu longer than anyone alive. And it isn't from here, that seed. It smells of somewhere cold and very far away, where something enormous is burning in its sleep.",
          [C("Sleep well, old guardian.", check=chk("Religion", 12, "o_bless_s", "o_bless_f"), once=True, tag="RELIGION"),
           C("I'll take it to Elder Maru.", "o_return3")], outcomes=[setf("dg1_hotaru_told")]),
        N("o_bless_s", "dg1_hotaru", "*Somewhere deep in the dark a great slow breath goes out, and every spirit-lantern in the grove burns a little warmer.* He heard you.", outcomes=[xp(100)], next="o_return3"),
        N("o_bless_f", "dg1_hotaru", "*Nothing answers but the drip of water. Hotaru pats your arm.* He's very deeply asleep. That's good, really.", next="o_return3"),
        N("o_return3", "dg1_hotaru", "Go on, up into the light. And the old lamplighters left a little something by the Root Lantern, for whoever tended it. That's you, now. Sort of. Honorary.", [C("Goodbye, Hotaru.")]),
        N("o_active", "dg1_hotaru", "He's past the two Sentinels, in the deepest hollow. Be kind if you can. Be quick if you can't. And mind the seed when it blooms — it makes the whole hollow go grey.",
          [C("Goodbye.")], [qstate(q, "warden")], "o_greet"),
        N("o_greet", "dg1_hotaru", "*A small spirit with a paper lantern for a head bobs out from behind a mushroom, glowing the colour of honey.* Oh! A person. A real one, with feet! I'm Hotaru. I tend the Root Lantern. Well. I tended it. Before.",
          [C("Before what? What happened down here?", "o_story"),
           C("The light down here is wrong. Something is draining it.", check=chk("Arcana", 13, "o_arc_s", "o_arc_f"), once=True, tag="ARCANA"),
           C("Goodbye.")]),
        N("o_arc_s", "dg1_hotaru", "*Her light flickers in surprise.* You can see it? Yes — it's being drunk, a little every night, by something that isn't from here. Clever person. Listen, then.", outcomes=[xp(80)], next="o_story"),
        N("o_arc_f", "dg1_hotaru", "Wrong? It's MOSS light. It's meant to be greenish. *She looks a little offended, then a little worried.* ...But you're right that something is wrong. Listen.", next="o_story"),
        N("o_story", "dg1_hotaru", "Last autumn something fell out of the sky and sank through the soil, all the way down to the roots: a seed, grey as ash, cold as a cellar step. Wherever it touched, the roots forgot themselves. So the Rootwarden — Kusu's guardian, big and gentle and terribly slow — swallowed it into his lantern heart, to keep it from the tree.",
          next="o_story2"),
        N("o_story2", "dg1_hotaru", "He's been holding it ever since, and it's been hollowing him out, a little every night. Now he doesn't know me. The rootlings run up into the orchards to get away from him. That singing you heard? That's him, trying to remember the words.",
          next="o_offer"),
        N("o_offer", "dg1_hotaru", "Will you free him? Take the seed out of his heart. He can't let go of it by himself — he's too stubborn, and too kind.",
          [C("I'll free him.", "o_accept", conds=[level(11), qnot(q)], outs=[start(q)]),
           C("What is this seed?", "o_seed"),
           C("I'm not ready to face him yet.")]),
        N("o_seed", "dg1_hotaru", "Grey, and cold, and patient. It came on the wind, from the north — I heard the ash hiss on the leaves the night it fell. The Hollow that came for your lanterns had the same smell. I think it's where the Hollow comes from. I think there are more of them, out there.",
          [C("I'll free him.", "o_accept", conds=[level(11), qnot(q)], outs=[start(q)]), C("I'm not ready to face him yet.")]),
        N("o_accept", "dg1_hotaru", "Thank you. He's past the Sentinels, in the deepest hollow. When the seed blooms the whole hollow goes grey — stand fast, it passes. And if you can, tell him Hotaru's waiting.", [C("Goodbye.")]),
    ]}


def write_all(root, jsonfmt):
    lv2 = {
        "_note": "Lanternvale's northern band (Docs/Expansion.md §8 row lv; prefix lv2): the Pipp orchard, the millpond, Hollyhock Farm and the west road. Quests: lv2_orchard_thieves (Hana Pipp), lv2_singing_roots (Nell; reveals the Root Hollows), lv2_lantern_oil (Tobben; Duskmane scouts). Generated by Tools/datagen/lv2/gen_lv2.py.",
        "creatures": CREATURES_LV2, "abilities": ABILITIES_LV2, "auras": AURAS_LV2, "lootTables": LOOT_LV2, "items": items_lv2(),
        "npcs": NPCS, "dialogues": [dlg_hana(), dlg_crate(), dlg_listening_root(), dlg_scouts(), dlg_teo(), dlg_ama(), dlg_sprout()],
        "quests": QUESTS_LV2,
    }
    dg1 = {
        "_note": "The Root Hollows (Docs/Expansion.md §8 row lv; prefix dg1): rootlings, root-wisps, greyed kodama, two elite packs and the Rootwarden (Boss). Creatures +1/+2 levels, floor 11/12, cap 13/14. Generated by Tools/datagen/lv2/gen_lv2.py.",
        "creatures": CREATURES_DG1, "abilities": ABILITIES_DG1, "auras": AURAS_DG1, "lootTables": LOOT_DG1, "items": items_dg1(),
        "npcs": NPCS_DG1, "dialogues": [dlg_hotaru()], "quests": QUESTS_DG1,
    }
    jsonfmt.write(os.path.join(root, "lv2_content.json"), lv2)
    jsonfmt.write(os.path.join(root, "dgn_root_hollows_content.json"), dg1)
