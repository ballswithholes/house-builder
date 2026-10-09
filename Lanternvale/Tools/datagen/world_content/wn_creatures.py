"""creatures.json (creatures + enemy-only cr_ abilities/auras) and loot.json."""
from wn_common import *

ABIL, AURAS, CREATURES, LOOT = [], [], [], []


def ab(id, name, icon, desc, effects, target="Enemy", school="Physical", melee=False, range_=0, cast=0.0,
       cd=0, area=None, hint="Damage", prio=3, tags=None, requires=None, time=None, min_range=0):
    a = {"id": id, "name": name, "icon": icon, "description": desc, "school": school, "hidden": True}
    if time: a["time"] = time
    if cast: a["castTime"] = cast
    if cd: a["cooldown"] = cd
    a["target"] = target
    if melee: a["melee"] = True
    if range_: a["range"] = range_
    if min_range: a["minRange"] = min_range
    if area: a["area"] = area
    if requires: a["requires"] = requires
    a["effects"] = effects
    if tags: a["tags"] = tags
    a["aiHint"] = hint
    a["aiPriority"] = prio
    ABIL.append(a)
    return id


def au(**kw):
    AURAS.append(kw)
    return kw["id"]


def circle(r, max_targets=0, affects="Enemies"):
    d = {"shape": "Circle", "radius": r, "centeredOnCaster": True, "affects": affects}
    if max_targets: d["maxTargets"] = max_targets
    return d


def AA(aura, target=None):
    d = {"type": "ApplyAura", "aura": aura}
    if target: d["target"] = target
    return d


# =========================================================================== auras
au(id="cr_hollow_touched", name="Hollow-Touched", icon="void", kind="Buff", school="Shadow", hidden=True,
   description="Emptied of light. Resistant to Shadow.", mods=[{"stat": "Resistance", "value": 10, "school": "Shadow"}])
au(id="cr_ravage_bleed", name="Ravaged", icon="blood", kind="Debuff", duration=15, tickInterval=3, tags=["Bleed"],
   description="Bleeding.", tickEffects=[{"type": "Damage", "min": 3, "perLevel": 0.4}])
au(id="cr_hollow_howl_debuff", name="Hollow Howl", icon="wolf", kind="Debuff", school="Shadow", dispel="Magic",
   duration=12, description="Damage dealt reduced by 10%.", mods=[pst("DamageDone", -10)])
au(id="cr_dazed", name="Dazed", icon="boot", kind="Debuff", duration=6, states=["Daze"],
   description="Movement speed reduced by 50%.", mods=[pst("MoveSpeed", -50)])
au(id="cr_spider_venom", name="Lurker Venom", icon="poison", kind="Debuff", school="Nature", dispel="Poison",
   duration=12, tickInterval=3, description="Nature damage every 3 sec.",
   tickEffects=[{"type": "Damage", "min": 2, "perLevel": 0.35}])
au(id="cr_webbed", name="Webbed", icon="spider", kind="Debuff", duration=6, states=["Root"],
   description="Stuck fast in sticky web.")
au(id="cr_mud_blind", name="Mud in Your Eye", icon="water_drop", kind="Debuff", duration=12,
   description="Chance to hit reduced by 10%.", mods=st(MeleeHit=-10, RangedHit=-10, SpellHit=-10))
au(id="cr_barkskin", name="Barkskin Blessing", icon="leaf", kind="Buff", school="Nature", dispel="Magic", duration=30,
   description="Armor increased by 50%.", mods=[pst("Armor", 50)])
au(id="cr_spore_sleep", name="Sleepy Spores", icon="leaf", kind="Debuff", school="Nature", dispel="Magic",
   duration=12, states=["Sleep"], breakOnDamage=True, description="Asleep. Any damage will wake the target.")
au(id="cr_gouged", name="Gouged", icon="fist", kind="Debuff", duration=6, states=["Incapacitate"],
   breakOnDamage=True, description="Incapacitated. Damage breaks the effect.")
au(id="cr_crippled", name="Crippled", icon="arrow", kind="Debuff", duration=12,
   description="Movement speed reduced by 50%.", mods=[pst("MoveSpeed", -50)])
au(id="cr_hex_weakness", name="Hex of Weakness", icon="curse", kind="Debuff", school="Shadow", dispel="Curse",
   duration=60, tags=["Curse"], description="Damage dealt reduced by 10%.", mods=[pst("DamageDone", -10)])
au(id="cr_rallied", name="Rallying Shout", icon="shout", kind="Buff", duration=60,
   description="Damage dealt increased by 15%.", mods=[pst("DamageDone", 15)])
au(id="cr_emptied", name="Emptied", icon="void", kind="Debuff", school="Shadow", dispel="Magic", duration=15,
   description="Healing taken and Spirit reduced by 20%.", mods=[pst("HealingTaken", -20), pst("Spirit", -20)])
au(id="cr_wail_fear", name="Hollow Wail", icon="fear", kind="Debuff", school="Shadow", dispel="Magic", duration=6,
   states=["Fear"], breakOnDamage=True, breakDamageThreshold=40, description="Fleeing in terror.")
au(id="cr_snuffed", name="Snuffed Out", icon="void", kind="Debuff", school="Shadow", dispel="Magic", duration=6,
   states=["Silence"], description="Silenced. Cannot cast spells.")
au(id="cr_rooted", name="Grasping Grey Roots", icon="leaf", kind="Debuff", school="Nature", dispel="Magic",
   duration=12, tickInterval=3, states=["Root"], description="Rooted. Nature damage every 3 sec.",
   tickEffects=[{"type": "Damage", "min": 3, "perLevel": 0.5}])
au(id="cr_blight_spore_dot", name="Blight Spores", icon="void", kind="Debuff", school="Nature", dispel="Disease",
   duration=12, tickInterval=3, description="Nature damage every 3 sec.",
   tickEffects=[{"type": "Damage", "min": 3, "perLevel": 0.5}])
au(id="cr_gore_bleed", name="Gored", icon="blood", kind="Debuff", duration=15, tickInterval=3, tags=["Bleed"],
   description="Bleeding.", tickEffects=[{"type": "Damage", "min": 3, "perLevel": 0.4}])
au(id="cr_hollowed", name="Hollowed", icon="void", kind="Debuff", school="Shadow", dispel="Magic", duration=15,
   description="The light inside you gutters. Healing taken reduced by 50%.", mods=[pst("HealingTaken", -50)])
au(id="cr_last_flicker", name="Last Flicker", icon="fire", kind="Buff", school="Shadow", duration=0,
   description="The Warden burns the last of its light. Damage dealt and attack speed increased by 25%.",
   mods=[pst("DamageDone", 25), pst("MeleeHaste", 25)])

# ======================================================================= abilities
WD = lambda pct=100, mn=0, mx=0, pl=0, ranged=False: {k: v for k, v in
                                                       {"type": "WeaponDamage", "weaponPct": pct, "min": mn or None,
                                                        "max": mx or None, "perLevel": pl or None,
                                                        "ranged": ranged or None}.items() if v is not None}
DMG = lambda mn, mx, pl, school=None, **k: {**{"type": "Damage", "min": mn, "max": mx, "perLevel": pl},
                                             **({"school": school} if school else {}), **k}

ab("cr_bite", "Bite", "fang", "Bites the target for weapon damage plus {0}.", [WD(100, 2, 4, 0.5)], melee=True, cd=6,
   prio=2)
ab("cr_ravage", "Ravage", "claw", "Tears into the target, causing it to bleed over 15 sec.",
   [WD(100), AA("cr_ravage_bleed")], melee=True, cd=15, prio=4)
ab("cr_hollow_howl", "Hollow Howl", "wolf", "A grey, echoing howl. Nearby enemies deal 10% less damage for 12 sec.",
   [AA("cr_hollow_howl_debuff")], target="Self", school="Shadow", area=circle(12), cd=30, hint="Debuff", prio=5)
ab("cr_call_the_pack", "Call the Pack", "wolf", "Howls for two packmates.",
   [{"type": "Summon", "summon": "cr_wolf_packmate", "count": 2, "target": "Self"}], target="Self", cd=600,
   hint="Summon", prio=10)
ab("cr_boar_charge", "Bristleback Charge", "charge", "Charges an enemy, dealing {1} damage and dazing it.",
   [{"type": "Charge"}, DMG(5, 8, 0.8), AA("cr_dazed")], range_=25, min_range=8,
   requires={"outOfMeleeRange": True}, cd=20, hint="Opener", prio=6)
ab("cr_venom_bite", "Venomous Bite", "spider", "A poisoned bite that deals Nature damage over 12 sec.",
   [WD(100), AA("cr_spider_venom")], melee=True, cd=12, prio=4)
ab("cr_web_spray", "Web Spray", "spider", "Roots the target in sticky web for 6 sec.", [AA("cr_webbed")],
   range_=20, cd=24, hint="CC", prio=5)
ab("cr_pebble_toss", "Pebble Toss", "rock", "Flings a carefully chosen pebble for {0} damage.", [DMG(4, 6, 0.6)],
   range_=20, prio=2)
ab("cr_mud_in_eye", "Mud in Your Eye", "water_drop", "Flicks mud into the target's eyes, reducing its chance to hit.",
   [AA("cr_mud_blind")], melee=True, cd=24, hint="Debuff", prio=5)
ab("cr_mossling_mend", "Mossy Mend", "leaf", "Packs fresh moss onto an ally's wounds, healing {0}.",
   [{"type": "Heal", "min": 16, "max": 22, "perLevel": 3}], target="Ally", school="Nature", range_=30, cast=2.0,
   hint="Heal", prio=8)
ab("cr_thorn_bolt", "Thorn Bolt", "nature", "Hurls a spray of thorns for {0} Nature damage.", [DMG(8, 11, 1.2)],
   school="Nature", range_=30, cast=2.0, prio=3)
ab("cr_barkskin_blessing", "Barkskin Blessing", "leaf", "Hardens an ally's skin, increasing armor by 50% for 30 sec.",
   [AA("cr_barkskin")], target="Ally", school="Nature", range_=30, cd=30, hint="Buff", prio=5)
ab("cr_sleepy_spores", "Sleepy Spores", "leaf", "Puffs drowsy spores at an enemy, putting it to sleep for 12 sec.",
   [AA("cr_spore_sleep")], school="Nature", range_=20, cast=1.5, cd=30, hint="CC", prio=6)
ab("cr_sinister_slash", "Dirty Slash", "dagger", "A cheap, effective slash for weapon damage plus {0}.",
   [WD(100, 3, 5, 0.8)], melee=True, prio=3)
ab("cr_gouge", "Gouge", "fist", "Incapacitates the target for 6 sec. Any damage breaks the effect.",
   [AA("cr_gouged")], melee=True, cd=30, hint="CC", prio=4)
ab("cr_piercing_shot", "Piercing Shot", "arrow", "A carefully aimed arrow for ranged weapon damage plus {0}.",
   [WD(110, 3, 5, 0.6, ranged=True)], range_=30, cd=12, prio=4)
ab("cr_crippling_shot", "Crippling Shot", "arrow", "An arrow to the leg. Movement speed reduced by 50% for 12 sec.",
   [WD(50, ranged=True), AA("cr_crippled")], range_=30, cd=20, hint="Debuff", prio=5)
ab("cr_hex_bolt", "Hex Bolt", "shadow", "Sends a bolt of crackling bad luck for {0} Shadow damage.",
   [DMG(12, 16, 1.4)], school="Shadow", range_=30, cast=2.5, prio=3)
ab("cr_hex_of_weakness", "Hex of Weakness", "curse", "Curses the target to deal 10% less damage for 60 sec.",
   [AA("cr_hex_weakness")], school="Shadow", range_=30, hint="Debuff", prio=5, tags=["Curse"])
ab("cr_dark_mend", "Dark Mend", "shadow", "Stitches an ally's wounds with shadow, healing {0}.",
   [{"type": "Heal", "min": 20, "max": 26, "perLevel": 3}], target="Ally", school="Shadow", range_=30, cast=2.5,
   hint="Heal", prio=8)
ab("cr_chief_cleave", "Toll Cleave", "axe", "A wide swing that hits up to 3 enemies in front for weapon damage plus {0}.",
   [WD(100, 4, 6, 0.6)], melee=True, area={"shape": "Cone", "radius": 6, "angle": 120, "maxTargets": 3,
                                            "affects": "Enemies"}, cd=12, hint="AoE", prio=6)
ab("cr_rallying_shout", "Rallying Shout", "shout", "Rusk bellows. Nearby allies deal 15% more damage for 60 sec.",
   [AA("cr_rallied", "AlliesInRadius") | {"radius": 30}, AA("cr_rallied", "Self")], target="Self", cd=60,
   hint="Buff", prio=8, tags=["Shout"])
ab("cr_toll_bash", "Toll Collector's Bash", "shield_bash", "Interrupts spellcasting and deals weapon damage.",
   [{"type": "Interrupt", "lockout": 4}, WD(60)], melee=True, cd=12, hint="Interrupt", prio=9, tags=["Interrupt"])
ab("cr_grey_bolt", "Grey Bolt", "void", "A bolt of colourless light for {0} Shadow damage.", [DMG(11, 15, 1.3)],
   school="Shadow", range_=30, cast=2.0, prio=3)
ab("cr_siphon_light", "Siphon Light", "drain", "Drinks the target's light for {0} Shadow damage, healing the caster.",
   [DMG(6, 9, 0.8, pctOfDamage=100)], school="Shadow", range_=30, cd=12, prio=5)
ab("cr_empty_touch", "Empty Touch", "void",
   "A cold touch that deals {0} Shadow damage and reduces healing taken and Spirit by 20%.",
   [DMG(8, 12, 1.0), AA("cr_emptied")], school="Shadow", melee=True, cd=15, prio=4)
ab("cr_hollow_wail", "Hollow Wail", "fear", "A wail of forgetting. Up to 2 nearby enemies flee in terror for 6 sec.",
   [AA("cr_wail_fear")], target="Self", school="Shadow", area=circle(8, 2), cd=30, hint="CC", prio=6)
ab("cr_snuff_out", "Snuff Out", "void", "Pinches out every flame nearby. Enemies within 12 yards are silenced for 6 sec.",
   [AA("cr_snuffed")], target="Self", school="Shadow", area=circle(12), cd=30, hint="CC", prio=7)
ab("cr_bark_slam", "Bark Slam", "rock", "A crushing blow that deals heavy damage and knocks the target back.",
   [WD(130), {"type": "Knockback", "distance": 8}], melee=True, cd=12, prio=5)
ab("cr_entangling_roots", "Grasping Grey Roots", "leaf", "Grey roots seize the target, rooting it for 12 sec.",
   [AA("cr_rooted")], school="Nature", range_=30, cast=1.5, cd=18, hint="CC", prio=6)
ab("cr_blight_spores", "Blight Spores", "void", "Exhales grey spores. Nearby enemies take Nature damage over 12 sec.",
   [AA("cr_blight_spore_dot")], target="Self", school="Nature", area=circle(10), cd=18, hint="AoE", prio=7)
# --- the Hollow Warden
ab("cr_lantern_requiem", "Lantern Requiem", "skull",
   "The Warden lowers its antlers and begins to sing every lantern out. After a long cast, deals {0} Shadow damage to "
   "every enemy in the sanctum and leaves them Hollowed. Interrupt it, stun it or silence it!",
   [DMG(25, 35, 2.5), AA("cr_hollowed")], target="Self", school="Shadow", cast=6.0, cd=30,
   area=circle(40), hint="AoE", prio=9, tags=["Telegraph"])
ab("cr_summon_hollow_wisps", "Call the Guttering Lights", "void",
   "The lanterns in the Warden's antlers spill out as two Hollow wisps.",
   [{"type": "Summon", "summon": "cr_hollow_wisp_minion", "count": 2, "target": "Self"}], target="Self",
   school="Shadow", cd=600, hint="Summon", prio=10)
ab("cr_antler_stomp", "Antler Stomp", "earth",
   "Rears and stomps, dealing {0} damage to enemies within 8 yards and dazing them.",
   [DMG(12, 18, 1.2), AA("cr_dazed")], target="Self", area=circle(8), cd=12, hint="AoE", prio=7)
ab("cr_gore", "Gore", "claw", "Gores the target with lantern-hung antlers, causing it to bleed.",
   [WD(120), AA("cr_gore_bleed")], melee=True, cd=10, prio=5)
ab("cr_dimming_gaze", "Dimming Gaze", "eye",
   "The Warden's empty eyes fix on a target: {0} Shadow damage and healing taken reduced by 50% for 15 sec.",
   [DMG(10, 14, 1.0), AA("cr_hollowed")], school="Shadow", range_=30, cd=15, hint="Debuff", prio=6)
ab("cr_last_flicker", "Last Flicker", "fire", "Below 25% health the Warden burns the last of its light.",
   [AA("cr_last_flicker", "Self")], target="Self", school="Shadow", cd=600, hint="Buff", prio=10, time="OffGcd")


# ======================================================================= creatures
def CA(ability, prio, chance=100, condition=""):
    d = {"ability": ability, "priority": prio}
    if chance != 100: d["chance"] = chance
    if condition: d["condition"] = condition
    return d


def creature(**kw):
    order = ["id", "name", "description", "sprite", "portrait", "type", "family", "rank", "levelMin", "levelMax",
             "scaleToParty", "levelOffset", "healthMult", "damageMult", "armorMult", "manaMult", "resource",
             "attackSpeed", "meleeSchool", "ranged", "rangedRange", "projectile", "moveSpeed", "size", "ai",
             "abilities", "passives", "immune", "stats", "lootTable", "xpMult", "faction", "tameable",
             "totemElement", "bark"]
    assert set(kw) <= set(order), set(kw) - set(order)
    kw.setdefault("scaleToParty", True)
    CREATURES.append({k: kw[k] for k in order if k in kw})


SHADOW_RES = lambda v: [{"stat": "Resistance", "value": v, "school": "Shadow"}]

creature(id="cr_training_dummy", name="Straw Training Dummy",
         description="Stuffed with straw, patched with sacking and wearing a turnip for a nose. It has never once "
                     "complained.", sprite="cr_training_dummy", type="Mechanical", rank="Normal", levelMin=1,
         levelMax=60, levelOffset=0, healthMult=500, damageMult=0, attackSpeed=2.0, moveSpeed=0, size=1.6,
         ai="Passive", immune=["Fear", "Polymorph", "Sleep", "Confuse", "Incapacitate"], xpMult=0,
         faction="Training", bark="*The dummy sways gently. Its turnip nose looks unimpressed.*")
creature(id="cr_wolf", name="Grey Wolf", description="Lean and hungry. The wolves never used to come this close to "
                                                      "the village.",
         sprite="cr_wolf", type="Beast", family="Wolf", rank="Normal", levelMin=2, levelMax=4, levelOffset=-1,
         attackSpeed=2.0, moveSpeed=11, size=1.1, ai="Melee", abilities=[CA("cr_bite", 2, 40)],
         lootTable="lt_wolf", faction="Wildlife", tameable=True,
         bark="*A low growl rolls out of the long grass.*")
creature(id="cr_wolf_packmate", name="Greymane's Packmate", description="Answers the old alpha's howl.",
         sprite="cr_wolf", type="Beast", family="Wolf", rank="Minion", levelMin=4, levelMax=6, levelOffset=0,
         attackSpeed=2.0, moveSpeed=11, size=1.0, ai="Melee", abilities=[CA("cr_bite", 2, 40)],
         xpMult=0, faction="Wildlife")
creature(id="cr_greymane", name="Greymane",
         description="The pasture wolves' old alpha, silver-ruffed and huge. The grey has crept into his eyes.",
         sprite="cr_wolf_greymane", portrait="cr_wolf_blighted", type="Beast", family="Wolf", rank="Elite", levelMin=6, levelMax=6, levelOffset=1,
         healthMult=1.2, damageMult=0.95, attackSpeed=2.0, moveSpeed=11, size=1.5, ai="Melee",
         abilities=[CA("cr_call_the_pack", 10, condition="selfHpBelow:50"), CA("cr_hollow_howl", 6, 50),
                    CA("cr_ravage", 5), CA("cr_bite", 3, 50)],
         passives=["cr_hollow_touched"], immune=["Fear"], lootTable="lt_greymane", faction="Wildlife",
         bark="*Greymane's eyes burn violet. The whole pack falls silent around him.*")
creature(id="cr_wolf_blighted", name="Blighted Wolf",
         description="Patches of its fur have gone the colour of cold ash. It does not seem to feel pain any more.",
         sprite="cr_wolf_blighted", type="Beast", family="Wolf", rank="Normal", levelMin=7, levelMax=9,
         levelOffset=1, attackSpeed=2.0, moveSpeed=11, size=1.2, ai="Melee",
         abilities=[CA("cr_hollow_howl", 5, 35), CA("cr_bite", 2, 40)], passives=["cr_hollow_touched"],
         lootTable="lt_wolf_blighted", faction="Hollow", bark="*It howls, and the howl has no echo.*")
creature(id="cr_boar", name="Bristleback Boar", description="All tusk, temper and terrible manners.",
         sprite="cr_boar", type="Beast", family="Boar", rank="Normal", levelMin=3, levelMax=6, levelOffset=0,
         healthMult=1.1, attackSpeed=2.0, moveSpeed=10, size=1.0, ai="Melee",
         abilities=[CA("cr_boar_charge", 6)], lootTable="lt_boar", faction="Wildlife", tameable=True,
         bark="*The boar paws the ground and snorts a challenge.*")
creature(id="cr_spider", name="Whisperwood Lurker", description="It spins its webs between the oldest trees and waits.",
         sprite="cr_spider", type="Beast", family="Spider", rank="Normal", levelMin=5, levelMax=7, levelOffset=0,
         attackSpeed=1.8, moveSpeed=8, size=0.9, ai="Melee",
         abilities=[CA("cr_web_spray", 5, 60, "targetNoAura:cr_webbed"), CA("cr_venom_bite", 4, 60)],
         lootTable="lt_spider", faction="Wildlife", tameable=True,
         bark="*Something many-legged clicks in the canopy.*")
creature(id="cr_mossling", name="Mossling Scamp",
         description="Knee-high, leaf-hatted and absolutely certain everything shiny belongs to it.",
         sprite="cr_mossling", type="Elemental", rank="Normal", levelMin=4, levelMax=6, levelOffset=0,
         healthMult=0.9, attackSpeed=1.8, moveSpeed=8, size=0.9, ai="Skirmisher",
         abilities=[CA("cr_mud_in_eye", 5, 50, "targetNoAura:cr_mud_blind"), CA("cr_pebble_toss", 3, 50)],
         lootTable="lt_mossling", faction="Mosslings", bark="Mine! Shiny! Go away!")
creature(id="cr_mossling_shaman", name="Mossling Mudcaller",
         description="Rattles a twig staff and mutters to the moss, which occasionally mutters back.",
         sprite="cr_mossling_shaman", type="Elemental", rank="Normal", levelMin=5, levelMax=7, levelOffset=0,
         healthMult=0.85, attackSpeed=2.0, meleeSchool="Nature", ranged=True, rangedRange=25, projectile="fx_bolt",
         moveSpeed=8, size=1.0, ai="Healer",
         abilities=[CA("cr_mossling_mend", 8, condition="allyHpBelow:60"), CA("cr_barkskin_blessing", 5, 40),
                    CA("cr_thorn_bolt", 3)],
         lootTable="lt_mossling_shaman", faction="Mosslings",
         bark="The moss remembers you! It does not like you!")
creature(id="cr_mossling_chief", name="Chief Puddlecap",
         description="The eldest Mossling, wearing the biggest hat in Whisperwood and a necklace of lantern wicks.",
         sprite="cr_mossling_shaman", type="Elemental", rank="Elite", levelMin=7, levelMax=7, levelOffset=1,
         healthMult=0.9, damageMult=0.8, attackSpeed=2.0, meleeSchool="Nature", ranged=True, rangedRange=25,
         projectile="fx_bolt", moveSpeed=8, size=1.2, ai="Healer",
         abilities=[CA("cr_mossling_mend", 8, condition="allyHpBelow:50"), CA("cr_sleepy_spores", 6, 50),
                    CA("cr_barkskin_blessing", 5, 40), CA("cr_thorn_bolt", 3)],
         lootTable="lt_puddlecap", faction="Mosslings",
         bark="You want wicks? Take them from Puddlecap's cold hands! ...They are always cold. Is a moss thing.")
creature(id="cr_bandit_cutthroat", name="Bridge Cutthroat",
         description="A scarf over the face and a knife in each hand. Neither is held quite right.",
         sprite="cr_bandit", type="Humanoid", rank="Normal", levelMin=7, levelMax=9, levelOffset=1,
         attackSpeed=1.8, moveSpeed=9, size=1.8, ai="Melee",
         abilities=[CA("cr_gouge", 5, 35), CA("cr_sinister_slash", 3, 60)], lootTable="lt_bandit",
         faction="Bandits", bark="Toll's gone up. Everything you're carrying, plus a bit.")
creature(id="cr_bandit_archer", name="Bridge Lookout", description="Keeps watch from the bridge rail with a patched shortbow.",
         sprite="cr_bandit_archer", type="Humanoid", rank="Normal", levelMin=7, levelMax=9, levelOffset=1,
         attackSpeed=2.4, ranged=True, rangedRange=30, projectile="fx_arrow", moveSpeed=9, size=1.8, ai="Ranged",
         abilities=[CA("cr_crippling_shot", 5, 60, "targetNoAura:cr_crippled"), CA("cr_piercing_shot", 4, 50)],
         lootTable="lt_bandit", faction="Bandits", bark="Got 'em in my sights! ...Which ones are they again?")
creature(id="cr_bandit_hexer", name="Bridge Hexer", description="Hung with charms, half of which are probably bottle caps.",
         sprite="cr_bandit_hexer", type="Humanoid", rank="Normal", levelMin=8, levelMax=9, levelOffset=1,
         healthMult=0.9, attackSpeed=2.0, meleeSchool="Shadow", ranged=True, rangedRange=30, projectile="fx_bolt",
         moveSpeed=9, size=1.8, ai="Caster",
         abilities=[CA("cr_dark_mend", 8, condition="allyHpBelow:50"),
                    CA("cr_hex_of_weakness", 5, condition="targetNoAura:cr_hex_weakness"), CA("cr_hex_bolt", 3)],
         lootTable="lt_bandit_hexer", faction="Bandits",
         bark="My charms say this ends badly. For you. Probably. They're a bit vague.")
creature(id="cr_bandit_chief", name="Rusk, the Toll",
         description="Broad as a barn door, with a farmer's hands and an axe he's still getting used to.",
         sprite="cr_bandit_chief", type="Humanoid", rank="Elite", levelMin=10, levelMax=10, levelOffset=2,
         healthMult=1.1, damageMult=1.0, attackSpeed=2.6, moveSpeed=9, size=2.0, ai="Melee",
         abilities=[CA("cr_toll_bash", 9, condition="targetCasting"), CA("cr_rallying_shout", 8),
                    CA("cr_chief_cleave", 6, condition="enemiesInRange:2"), CA("cr_sinister_slash", 3, 60)],
         lootTable="lt_bandit_chief", faction="Bandits", bark="Right. The expensive way, then.")
creature(id="cr_hollow_wisp", name="Hollow Wisp",
         description="A lantern flame with the warmth scooped out. It drifts toward light like a moth toward a memory.",
         sprite="cr_hollow_wisp", type="Undead", rank="Normal", levelMin=8, levelMax=10, levelOffset=1,
         healthMult=0.8, attackSpeed=2.0, meleeSchool="Shadow", ranged=True, rangedRange=30, projectile="fx_bolt",
         moveSpeed=9, size=1.0, ai="Caster",
         abilities=[CA("cr_siphon_light", 5, 50), CA("cr_grey_bolt", 3)], passives=["cr_hollow_touched"],
         lootTable="lt_hollow_wisp", faction="Hollow",
         bark="*The wisp flickers. For a moment it almost looks like a lantern flame again.*")
creature(id="cr_hollow_wisp_minion", name="Guttering Light",
         description="A lantern flame torn loose from the Warden's antlers.", sprite="cr_hollow_wisp",
         type="Undead", rank="Minion", levelMin=10, levelMax=12, levelOffset=0, attackSpeed=2.0,
         meleeSchool="Shadow", ranged=True, rangedRange=30, projectile="fx_bolt", moveSpeed=9, size=0.8, ai="Caster",
         abilities=[CA("cr_grey_bolt", 3)], passives=["cr_hollow_touched"], xpMult=0, faction="Hollow")
creature(id="cr_hollow_spirit", name="Hollowed Pilgrim",
         description="A pilgrim's spirit with the light scooped out. It still walks the stair, though it has "
                     "forgotten why.",
         sprite="cr_hollow_spirit", type="Undead", rank="Normal", levelMin=9, levelMax=11, levelOffset=1,
         attackSpeed=2.2, meleeSchool="Shadow", moveSpeed=8, size=1.9, ai="Melee",
         abilities=[CA("cr_hollow_wail", 6, condition="enemiesInRange:2"), CA("cr_empty_touch", 4, 60)],
         passives=["cr_hollow_touched"], lootTable="lt_hollow_spirit", faction="Hollow",
         bark="...was I... going somewhere...?")
creature(id="cr_hollow_keeper", name="Keeper Ishiro, Hollowed",
         description="The Old Shrine's last keeper. He tended the Heart Lantern for forty years, and now he tends "
                     "its darkness.",
         sprite="cr_hollow_spirit", type="Undead", rank="Elite", levelMin=11, levelMax=11, levelOffset=2,
         healthMult=1.0, damageMult=0.95, attackSpeed=2.2, meleeSchool="Shadow", ranged=True, rangedRange=30,
         projectile="fx_bolt", moveSpeed=8, size=2.1, ai="Caster",
         abilities=[CA("cr_snuff_out", 7, condition="enemiesInRange:2"), CA("cr_siphon_light", 5, 60),
                    CA("cr_grey_bolt", 3)],
         passives=["cr_hollow_touched"], immune=["Fear"], lootTable="lt_keeper", faction="Hollow",
         bark="The lanterns... must be put out... so it cannot... take them...")
creature(id="cr_hollow_treant", name="Rotheart, the Blighted Treant",
         description="Once a guardian of the pilgrim stair. Now a knot of grey wood with a slow, angry heartbeat.",
         sprite="cr_hollow_treant", type="Elemental", rank="Elite", levelMin=10, levelMax=10, levelOffset=2,
         healthMult=1.3, damageMult=1.0, armorMult=1.25, attackSpeed=3.0, moveSpeed=6, size=2.6, ai="Melee",
         abilities=[CA("cr_blight_spores", 7, condition="enemiesInRange:2"),
                    CA("cr_entangling_roots", 6, condition="targetNoAura:cr_rooted"), CA("cr_bark_slam", 5)],
         passives=["cr_hollow_touched"], immune=["Fear", "Polymorph", "Sleep"], lootTable="lt_rotheart",
         faction="Hollow",
         bark="*Rotheart's roots heave. Deep inside, old wood groans like a sleeper caught in a nightmare.*")
creature(id="cr_hollow_warden", name="The Hollow Warden",
         description="The great stag spirit who walked the valley every autumn to relight its lanterns. Starved of "
                     "flame, it has begun to drink them instead. Its antler-lanterns burn grey.",
         sprite="cr_hollow_warden", type="Spirit", rank="Boss", levelMin=12, levelMax=12, levelOffset=2,
         healthMult=1.15, damageMult=0.9, armorMult=0.95, attackSpeed=2.5, moveSpeed=8, size=4.5, ai="Boss",
         abilities=[CA("cr_last_flicker", 10, condition="selfHpBelow:25"),
                    CA("cr_summon_hollow_wisps", 10, condition="selfHpBelow:50"),
                    CA("cr_lantern_requiem", 9),
                    CA("cr_antler_stomp", 7, condition="enemiesInRange:2"),
                    CA("cr_dimming_gaze", 6, condition="targetNoAura:cr_hollowed"),
                    CA("cr_gore", 5)],
         passives=["cr_hollow_touched"], immune=["Fear", "Polymorph", "Sleep", "Incapacitate", "Confuse", "Banish"],
         stats=SHADOW_RES(15), lootTable="lt_warden", xpMult=1.3, faction="Hollow",
         bark="*The Warden raises its head. Every lantern in its antlers gutters grey, and the shrine goes very, "
              "very quiet.*")


# ====================================================================== loot tables
def E(item, chance=100, mn=1, mx=1, quest=False):
    d = {"item": item}
    if quest: d["whileQuestNeeds"] = True   # quest item: drops only while a quest still collects it
    if chance != 100: d["chance"] = chance
    if mn != 1 or mx != 1: d["min"], d["max"] = mn, mx
    return d


def RND(chance, quality="Uncommon", offset=0):
    d = {"random": True, "chance": chance, "quality": quality}
    if offset: d["itemLevelOffset"] = offset
    return d


def lt(id, gmin, gmax, entries, rolls=1):
    d = {"id": id, "goldMin": gmin, "goldMax": gmax, "entries": entries}
    if rolls != 1: d["rolls"] = rolls
    LOOT.append(d)


lt("lt_wolf", 0, 0, [E("junk_broken_fang", 45), E("junk_ruined_pelt", 35), RND(2)])
lt("lt_wolf_blighted", 0, 0, [E("junk_grey_ash", 30), E("junk_broken_fang", 30), E("junk_ruined_pelt", 25), RND(3)])
lt("lt_greymane", 0, 0, [E("greymanes_mantle"), E("junk_grey_ash"), E("wolf_tooth_necklace", 25), RND(35)])
lt("lt_boar", 0, 0, [E("junk_cracked_tusk", 40), E("junk_bristly_hide", 35), RND(2)])
lt("lt_spider", 0, 0, [E("junk_sticky_web", 45), E("junk_chitin_fragment", 30), E("spiderhide_bracers", 2), RND(3)])
lt("lt_mossling", 6, 24, [E("lantern_wick", quest=True), E("junk_tiny_leaf_hat", 25), E("junk_damp_pebbles", 25),
                          E("junk_nibbled_mushroom", 20), E("food_mushroom_skewer", 8), E("junk_shiny_button", 5),
                          RND(3)])
lt("lt_mossling_shaman", 8, 30, [E("lantern_wick", quest=True), E("junk_tiny_leaf_hat", 20), E("junk_damp_pebbles", 20),
                                 E("potion_minor_mana", 6), E("mossweave_sandals", 2), RND(4)])
lt("lt_puddlecap", 60, 140, [E("lantern_wick", 100, 4, 4, quest=True), E("mossweave_sandals"), E("junk_shiny_button"), RND(30)])
lt("lt_bandit", 30, 90, [E("junk_torn_scarf_mask", 25), E("junk_chipped_dice", 12), E("junk_dented_cup", 15),
                         E("junk_bad_poetry", 6), E("food_rice_ball", 10), E("drink_spring_water", 10),
                         E("potion_minor_healing", 6), E("bandage_linen", 8), E("scroll_strength", 3),
                         E("scroll_agility", 3), E("cutthroats_hood", 2), E("bandits_gutting_knife", 2),
                         E("cutpurses_signet", 2), RND(4)])
lt("lt_bandit_hexer", 40, 100, [E("junk_chipped_dice", 15), E("junk_bad_poetry", 10), E("potion_minor_mana", 8),
                                E("scroll_intellect", 5), E("hexers_charm_robe", 4), E("hexers_fetish_wand", 4),
                                RND(4)])
lt("lt_bandit_chief", 300, 600, [E("rusks_toll_taker"), E("bridgekeepers_glaive", 25), E("potion_lesser_healing"),
                                 RND(50)])
lt("lt_hollow_wisp", 0, 0, [E("spirit_ember", quest=True), E("junk_grey_ash", 40), E("junk_dull_lantern_glass", 25), RND(3)])
lt("lt_hollow_spirit", 40, 110, [E("junk_faded_prayer_strip", 35), E("junk_grey_ash", 30), E("junk_hollow_shard", 15),
                                 E("potion_lesser_healing", 6), E("scroll_spirit", 3), E("scroll_stamina", 3),
                                 E("bindings_of_quiet_dusk", 2), RND(5)])
lt("lt_keeper", 200, 400, [E("ishiros_prayer_beads"), E("junk_faded_prayer_strip"), RND(50)])
lt("lt_rotheart", 0, 0, [E("heartwood_bulwark"), E("junk_blighted_bark"), E("thornwood_staff", 30), RND(40)])
lt("lt_warden", 1500, 2500, [E("lanternbough"), E("antler_lantern_charm"), RND(100, "Uncommon", 1),
                             RND(35, "Rare")])
# chests
lt("lt_chest_forest", 80, 250, [E("potion_minor_healing", 50, 1, 2), E("food_mushroom_skewer", 40, 1, 3),
                                E("drink_barley_tea", 30, 1, 3), E("scroll_stamina", 15), E("scroll_spirit", 15),
                                RND(50)])
lt("lt_chest_forest_locked", 200, 500, [E("potion_lesser_healing", 60, 1, 2), E("potion_minor_mana", 40),
                                        E("elixir_minor_agility", 30), E("elixir_lions_strength", 30),
                                        E("scroll_protection", 30), RND(100)])
lt("lt_chest_spider_hollow", 20, 60, [E("junk_sticky_web", 80, 1, 3), E("junk_bent_spoon", 30), RND(25)])
lt("lt_chest_moppet", 150, 150, [E("junk_shiny_button"), E("valley_brass_band"), E("food_honey_cake", 100, 2, 2)])
lt("lt_chest_village", 40, 120, [E("potion_minor_healing", 100, 1, 2), E("food_rice_ball", 100, 2, 3),
                                 E("junk_shiny_button", 30)])
lt("lt_chest_shrine", 300, 700, [E("potion_healing", 40), E("potion_lesser_mana", 30), E("elixir_wisdom", 20),
                                 RND(100), RND(10, "Rare")])
lt("lt_chest_offering", 600, 1000, [E("elixir_lantern_oil", 100, 2, 2), E("potion_healing"), RND(100), RND(50, "Rare")])


def build_creatures():
    return {"_note": "Enemies of the Lanternvale slice. All hostile creatures use scaleToParty + levelOffset (level = "
                     "clamp(partyLevel + levelOffset, 1, 63)); levelMin/levelMax document the intended band for a fresh "
                     "level-1 playthrough. health/damage/armor/xp multipliers are ON TOP of the engine's rank multipliers. Enemy-only abilities "
                     "and auras are prefixed cr_.",
            "creatures": CREATURES, "abilities": ABIL, "auras": AURAS}


def build_loot():
    return {"_note": "Creature and chest loot tables. Gold in copper. Entries roll independently (chance in %).",
            "lootTables": LOOT}
