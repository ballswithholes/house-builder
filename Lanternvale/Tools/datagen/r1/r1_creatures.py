"""The Hollow Heart's creatures: Elite trash (rootlings, blight hounds, hollow knights), boss adds, and four bosses.

Levels: scaleToParty with offset +1, trash floor 21 cap 24, bosses floor 22 cap 24 (a raid of level 22 meets level 23).
Raid maps keep encounter health at x1 for any party size (GameSession.EncounterHealthScale), so the health here is tuned
for ten: a good raid of ten at 22 wins through AutoResolve, five at the same level do not (TestsContentR1).
"""

IMMUNE_BOSS = ["Fear", "Polymorph", "Sleep", "Incapacitate", "Confuse", "Banish", "Stun"]

# tuning knobs (TestsContentR1 checks the outcome)
TRASH_HP = {"rootling": 1.6, "hound": 1.7, "knight": 2.1}
BOSS = {
    "thornmaw": dict(hp=5.0, dmg=2.4, berserk=18),
    "sorrow": dict(hp=2.7, dmg=2.1, berserk=22),
    "solace": dict(hp=2.3, dmg=1.6, berserk=22),
    "mother_mire": dict(hp=4.8, dmg=2.3, berserk=20),
    "daughter": dict(hp=1.5, dmg=1.3),
    "heart": dict(hp=8.5, dmg=1.8, berserk=36),
}
TRASH_DMG = 1.35
TRASH_XP, BOSS_XP = 0.35, 0.9


def ab(cid, priority, condition="", chance=None):
    d = {"ability": cid, "priority": priority}
    if condition:
        d["condition"] = condition
    if chance is not None:
        d["chance"] = chance
    return d


def creature(cid, name, sprite, portrait, ctype, rank, size, desc, abilities, *, hp=1.0, dmg=1.0, armor=1.0, ai="Melee",
             floor=21, cap=24, offset=1, material="", voice="", loot="", xp=TRASH_XP, bark="", ranged=False, rng=30,
             school=None, speed=None, attack=2.0, immune=None, stats=None, passives=None, family="", mana=False):
    c = {"id": cid, "name": name, "description": desc, "sprite": sprite, "portrait": portrait, "type": ctype}
    if family:
        c["family"] = family
    c.update({"rank": rank, "levelMin": floor, "levelMax": cap, "scaleToParty": True, "levelOffset": offset,
              "levelFloor": floor, "levelCap": cap, "healthMult": hp, "damageMult": dmg})
    if armor != 1.0:
        c["armorMult"] = armor
    if mana:
        c["resource"] = "Mana"
    c["attackSpeed"] = attack
    if school:
        c["meleeSchool"] = school
    if ranged:
        c["ranged"] = True
        c["rangedRange"] = rng
    if speed is not None:
        c["moveSpeed"] = speed
    c["size"] = size
    c["ai"] = ai
    c["abilities"] = abilities
    if passives:
        c["passives"] = passives
    if immune:
        c["immune"] = immune
    if stats:
        c["stats"] = stats
    if loot:
        c["lootTable"] = loot
    c["xpMult"] = xp
    c["faction"] = "Hollow"
    if bark:
        c["bark"] = bark
    c["material"] = material
    c["voice"] = voice
    return c


def berserk(b):
    """The hard enrage: a raid that is too slow (or too few) is swept away."""
    return ab("cr_r1_berserk", 11, f"roundAtLeast:{b['berserk']}")


def fury():
    """While berserk, the whole chamber is swept every round."""
    return ab("cr_r1_hollow_fury", 11, "selfAura:cr_r1_berserk")


def creatures():
    B = BOSS
    return [
        # ---------------------------------------------------------------- trash (Elite)
        creature("cr_r1_rootling", "Hollow Rootling", "cr_rootling", "cr_mossling", "Elemental", "Elite", 1.1,
                 "A root-tuber of the old grove, woken wrong. Its eyes glow amber, and its bud glows a sickly violet.",
                 [ab("cr_r1_root_headbutt", 6), ab("cr_r1_sap_spores", 5, "targetNoAura:cr_r1_sap_spores")],
                 hp=TRASH_HP["rootling"], dmg=TRASH_DMG, material="wood", voice="wood", loot="lt_r1_trash",
                 bark="*Creak. Creak-creak.* (It sounds like a floorboard that hates you.)"),
        creature("cr_r1_blight_hound", "Blight Hound", "cr_blight_hound", "cr_wolf_blighted", "Beast", "Elite", 1.6,
                 "A gaunt hound with a bone mask and crystals growing through its mane. It hunts by the light you carry.",
                 [ab("cr_r1_blight_bite", 6), ab("cr_r1_hollow_howl", 7, "selfNoAura:cr_r1_hollow_howl")],
                 hp=TRASH_HP["hound"], dmg=TRASH_DMG, material="fur", voice="beast", loot="lt_r1_trash", family="Wolf",
                 bark="*A howl like a lantern going out.*"),
        creature("cr_r1_hollow_knight", "Hollow Knight", "cr_hollow_knight", "cr_hollow_spirit", "Undead", "Elite", 2.2,
                 "Empty armour that still keeps a vigil. Its lantern burns violet now, and it no longer remembers for whom.",
                 [ab("cr_r1_vigil_ward", 9, "selfHpBelow:45"), ab("cr_r1_hollow_cleave", 6, "enemiesInRange:2"),
                  ab("cr_r1_dusk_lantern", 5)],
                 hp=TRASH_HP["knight"], dmg=TRASH_DMG, armor=1.3, material="plate", voice="spirit", loot="lt_r1_trash",
                 bark="Halt. ...Halt. Who goes... who went..."),
        # ---------------------------------------------------------------- adds (Minion)
        creature("cr_r1_thornling", "Thornling", "cr_rootling", "cr_mossling", "Elemental", "Minion", 0.8,
                 "A sprout of Thornmaw's bramble, all prickles and spite.",
                 [ab("cr_r1_thorn_jab", 5)], hp=2.2, xp=0, material="wood", voice="wood"),
        creature("cr_r1_weeping_wisp", "Weeping Wisp", "cr_fen_wisp", "cr_hollow_wisp", "Spirit", "Minion", 1.1,
                 "A single tear that forgot to fall, and kept going.",
                 [ab("cr_r1_tear_bolt", 5)], hp=2.4, xp=0, ai="Caster", ranged=True, school="Frost", material="ether",
                 voice="spirit"),
        creature("cr_r1_mirespawn", "Mirespawn", "cr_bog_ghoul", "cr_mossling", "Undead", "Minion", 1.8,
                 "Something Mother Mire stirred up out of the bottom of the pot.",
                 [ab("cr_r1_mire_claw", 5)], hp=2.6, xp=0, material="wet", voice="beast"),
        creature("cr_r1_hollow_seedling", "Seed of the Hollow", "cr_rootling", "cr_hollow_wisp", "Elemental", "Minion", 1.4,
                 "A grey sprout of the Hollow, born from the heart in a single beat. It grows towards whatever is brightest.",
                 [ab("cr_r1_seedling_grasp", 5)], hp=2.6, xp=0, school="Shadow", material="wood", voice="wood"),
        creature("cr_r1_hollowed_light", "Hollowed Light", "cr_hollow_wisp", "cr_hollow_wisp", "Spirit", "Minion", 1.0,
                 "One of Mirefen's drowned lantern-lights, turned inside out by the heart. It does not want to hurt you. It does anyway.",
                 [ab("cr_r1_grey_flicker", 5)], hp=2.0, xp=0, ai="Caster", ranged=True, school="Shadow", material="ether",
                 voice="spirit"),
        creature("cr_r1_coven_daughter", "Daughter of the Mire", "cr_mire_hag", "cr_bandit_hexer", "Humanoid", "Elite", 2.1,
                 "One of Mother Mire's eldest daughters, with a frog on her hat and a ladle in her belt. Both are for hitting.",
                 [ab("cr_r1_coven_mend", 9, "allyHpBelow:70"), ab("cr_r1_coven_hex", 5)],
                 hp=B["daughter"]["hp"], dmg=B["daughter"]["dmg"], ai="Healer", ranged=True, school="Nature", mana=True,
                 material="cloth", voice="humanoid", xp=TRASH_XP, loot="lt_r1_trash",
                 bark="Mother! Mother, there's more of them, and they've got *boots on*!"),
        # ---------------------------------------------------------------- bosses
        creature("cr_r1_thornmaw", "Thornmaw the Rootbound", "cr_r1_thornmaw", "cr_hollow_treant", "Elemental", "Boss", 6.0,
                 "The grove's oldest root-beast, once a gentle digger of springs. The Hollow wrapped it round a grey cinder, and now it guards the way with every thorn it has.",
                 [berserk(B["thornmaw"]), fury(), ab("cr_r1_rootbound_frenzy", 10, "selfHpBelow:25"), ab("cr_r1_call_thornlings", 10, "selfHpBelow:70"),
                  ab("cr_r1_call_thornlings_deep", 10, "selfHpBelow:35"), ab("cr_r1_bramble_burst", 9, "roundAtLeast:2"),
                  ab("cr_r1_grasping_roots", 7), ab("cr_r1_thornmaw_bite", 6)],
                 hp=B["thornmaw"]["hp"], dmg=B["thornmaw"]["dmg"], armor=1.2, ai="Boss", floor=22, cap=24, attack=2.5, speed=6,
                 immune=IMMUNE_BOSS, stats=[{"stat": "Resistance", "value": 20, "school": "Nature"}], loot="lt_r1_thornmaw",
                 xp=BOSS_XP, material="wood", voice="wood",
                 bark="*The thorns stand up along its back like the hair on a cat. Four violet eyes open, one after another.*"),
        creature("cr_r1_twin_sorrow", "Sorrow, the Weeping Twin", "cr_r1_twin_sorrow", "cr_hollow_spirit", "Spirit", "Boss", 3.2,
                 "One of the grove's twin guardians, who once kept its grief so nobody else had to. The Hollow gave her far too much to keep.",
                 [berserk(B["sorrow"]), fury(), ab("cr_r1_grief_unbound", 10, "selfHpBelow:30"), ab("cr_r1_weeping_wisps", 10, "selfHpBelow:60"),
                  ab("cr_r1_veil_of_tears", 9, "roundAtLeast:2"), ab("cr_r1_drowning_sorrow", 6), ab("cr_r1_sorrow_bolt", 3)],
                 hp=B["sorrow"]["hp"], dmg=B["sorrow"]["dmg"], ai="Caster", ranged=True, school="Frost", mana=True, floor=22, cap=24,
                 immune=IMMUNE_BOSS, stats=[{"stat": "Resistance", "value": 20, "school": "Frost"}], loot="lt_r1_twins", xp=BOSS_XP,
                 material="ether", voice="spirit", bark="Shh. Shh. Let us weep for you first. It is easier, after."),
        creature("cr_r1_twin_solace", "Solace, the Weeping Twin", "cr_r1_twin_solace", "cr_hollow_spirit", "Spirit", "Boss", 3.2,
                 "The other twin, who kept the grove's comfort. She still comforts. She comforts her sister, who is trying to drown you.",
                 [berserk(B["solace"]), fury(), ab("cr_r1_last_light", 10, "selfHpBelow:30"), ab("cr_r1_solace_mending", 9, "allyHpBelow:85"),
                  ab("cr_r1_lullaby", 7), ab("cr_r1_dawn_lantern", 3)],
                 hp=B["solace"]["hp"], dmg=B["solace"]["dmg"], ai="Healer", ranged=True, school="Holy", mana=True, floor=22, cap=24,
                 immune=IMMUNE_BOSS, stats=[{"stat": "Resistance", "value": 20, "school": "Holy"}], xp=BOSS_XP,
                 material="ether", voice="spirit", bark="I am sorry. I am so sorry. Hold still, it will not hurt for long."),
        creature("cr_r1_mother_mire", "Mother Mire", "cr_r1_mother_mire", "cr_hollow_treant", "Humanoid", "Boss", 5.5,
                 "The coven's mother, as old as the fen and twice as deep. She found the Hollow seed drinking lights in the dark, and she fed it, and she has been very proud of it ever since.",
                 [berserk(B["mother_mire"]), fury(), ab("cr_r1_boiling_over", 10, "selfHpBelow:20"), ab("cr_r1_call_mirespawn", 10, "selfHpBelow:75"),
                  ab("cr_r1_call_mirespawn_deep", 10, "selfHpBelow:40"), ab("cr_r1_witchs_brew", 9, "selfHpBelow:60"),
                  ab("cr_r1_bog_eruption", 8, "roundAtLeast:2"), ab("cr_r1_frog_hex", 7), ab("cr_r1_cauldron_bolt", 3)],
                 hp=B["mother_mire"]["hp"], dmg=B["mother_mire"]["dmg"], ai="Caster", ranged=True, school="Nature", mana=True,
                 floor=22, cap=24, speed=6, immune=IMMUNE_BOSS, stats=[{"stat": "Resistance", "value": 25, "school": "Nature"}],
                 loot="lt_r1_mother_mire", xp=BOSS_XP, material="cloth", voice="humanoid",
                 bark="Supper's on, my dearies! Oh, don't look like that. *You're* not supper. You're the *seasoning.*"),
        creature("cr_r1_hollow_heart", "The Hollow Heart", "cr_r1_hollow_heart", "cr_hollow_wisp", "Spirit", "Boss", 7.5,
                 "The grove's heart lantern, with a grey seed grown through it: a seed that came south on ash from the north and learned to drink light. It beats with the borrowed glow of every lantern that ever drowned in Mirefen.",
                 [berserk(B["heart"]), fury(), ab("cr_r1_final_beat", 10, "selfHpBelow:20"), ab("cr_r1_hollowed_lights", 10, "selfHpBelow:50"),
                  ab("cr_r1_sprout_seedlings", 9, "roundAtLeast:2"), ab("cr_r1_drink_the_light", 8, "roundAtLeast:3"),
                  ab("cr_r1_heartbeat", 7), ab("cr_r1_grasping_lash", 5)],
                 hp=B["heart"]["hp"], dmg=B["heart"]["dmg"], armor=1.1, ai="Boss", ranged=True, rng=40, school="Shadow", floor=22,
                 cap=24, speed=0, attack=2.5, immune=IMMUNE_BOSS + ["Root"], stats=[{"stat": "Resistance", "value": 25, "school": "Shadow"}],
                 loot="lt_r1_hollow_heart", xp=BOSS_XP * 1.25, material="wood", voice="spirit",
                 bark="*BOOM. ...boom.* ...light... more... light..."),
    ]


def A(aid, name, icon, desc, school, effects, *, target="Enemy", cast=0.0, cd=0.0, rng=0, melee=False, area=None,
      tags=None, hint="", prio=0, time=None):
    a = {"id": aid, "name": name, "icon": icon, "description": desc, "school": school, "hidden": True}
    if time:
        a["time"] = time
    if cast:
        a["castTime"] = cast
    if cd:
        a["cooldown"] = cd
    a["target"] = target
    if rng:
        a["range"] = rng
    if melee:
        a["melee"] = True
    if area:
        a["area"] = area
    a["effects"] = effects
    if tags:
        a["tags"] = tags
    if hint:
        a["aiHint"] = hint
    if prio:
        a["aiPriority"] = prio
    return a


def circle(r, affects="Enemies"):
    return {"shape": "Circle", "radius": r, "centeredOnCaster": True, "affects": affects}


def dmg(lo, hi, per, school=None):
    e = {"type": "Damage", "min": lo, "max": hi, "perLevel": per}
    if school:
        e["school"] = school
    return e


def wdmg(pct, lo, hi, per):
    return {"type": "WeaponDamage", "weaponPct": pct, "min": lo, "max": hi, "perLevel": per}


def aura_on(aid, target=None):
    e = {"type": "ApplyAura", "aura": aid}
    if target:
        e["target"] = target
    return e


def summon(cid, n):
    return {"type": "Summon", "summon": cid, "count": n, "target": "Self"}


def abilities():
    return [
        # ---- trash
        A("cr_r1_root_headbutt", "Root Headbutt", "earth", "A hard knock with a woody head: weapon damage plus {0}.", "Physical",
          [wdmg(100, 6, 9, 0.9)], cd=6, melee=True, hint="Damage", prio=5),
        A("cr_r1_sap_spores", "Sap Spores", "poison", "A puff of grey spores that settle in the lungs: Nature damage every 3 sec for 12 sec.",
          "Nature", [aura_on("cr_r1_sap_spores")], cd=12, rng=10, hint="Debuff", prio=4),
        A("cr_r1_blight_bite", "Blight Bite", "fang", "A bite that leaves crystal splinters behind: weapon damage plus {0}.", "Physical",
          [wdmg(110, 8, 12, 1.0)], cd=6, melee=True, hint="Damage", prio=5),
        A("cr_r1_hollow_howl", "Hollow Howl", "shout", "A howl that makes the pack braver: damage dealt by nearby allies increased by 15% for 18 sec.",
          "Shadow", [aura_on("cr_r1_hollow_howl")], cd=30, target="Self", area=circle(15, "Allies"), hint="Buff", prio=7),
        A("cr_r1_hollow_cleave", "Hollow Cleave", "axe", "A wide, slow sweep of an old longsword: weapon damage plus {0} to up to 3 enemies in front.",
          "Physical", [wdmg(100, 8, 12, 1.0)], cd=8, melee=True,
          area={"shape": "Cone", "radius": 6, "angle": 120, "maxTargets": 3, "affects": "Enemies"}, hint="AoE", prio=6),
        A("cr_r1_dusk_lantern", "Dusk Lantern", "shadow", "The knight's lantern spits violet fire: {0} Shadow damage.", "Shadow",
          [dmg(22, 28, 1.6)], cast=2.0, rng=30, hint="Damage", prio=4),
        A("cr_r1_vigil_ward", "Vigil Ward", "shield", "The knight closes ranks with nobody: absorbs damage for 12 sec.", "Physical",
          [aura_on("cr_r1_vigil_ward", "Self")], cd=30, target="Self", hint="Defensive", prio=9),
        # ---- adds
        A("cr_r1_thorn_jab", "Thorn Jab", "claw", "A jab with a fistful of thorns: weapon damage plus {0}.", "Physical",
          [wdmg(100, 4, 6, 0.6)], cd=6, melee=True, hint="Damage", prio=4),
        A("cr_r1_tear_bolt", "Tear Bolt", "water_drop", "A single cold tear, thrown hard: {0} Frost damage.", "Frost",
          [dmg(16, 20, 1.2)], cast=2.0, rng=30, hint="Damage", prio=4),
        A("cr_r1_mire_claw", "Mire Claw", "claw", "A muddy swipe: weapon damage plus {0}.", "Physical",
          [wdmg(100, 5, 8, 0.7)], cd=6, melee=True, hint="Damage", prio=4),
        A("cr_r1_seedling_grasp", "Grey Grasp", "tentacle", "A grey tendril wraps a wrist and pulls: weapon damage plus {0}.", "Shadow",
          [wdmg(100, 5, 8, 0.7)], cd=6, melee=True, hint="Damage", prio=4),
        A("cr_r1_grey_flicker", "Grey Flicker", "void", "A drowned light flickers the wrong colour: {0} Shadow damage.", "Shadow",
          [dmg(16, 20, 1.2)], cast=2.0, rng=30, hint="Damage", prio=4),
        A("cr_r1_coven_mend", "Coven Mending", "heal_plus", "A ladle of something green and steaming: heals an ally for 6% of its health. It can be interrupted.",
          "Nature", [{"type": "Heal", "min": 0, "max": 0, "pctOfMax": 6}], cast=2.5, cd=12, target="Ally", rng=30,
          tags=["Telegraph", "Heal"], hint="Heal", prio=9),
        A("cr_r1_coven_hex", "Coven Hex", "curse", "A muttered word and a wagging finger: {0} Nature damage.", "Nature",
          [dmg(18, 24, 1.4)], cast=2.0, rng=30, hint="Damage", prio=4),
        # ---- Thornmaw
        A("cr_r1_thornmaw_bite", "Rootbound Maw", "fang", "Thornmaw bites down with a maw full of thorns: heavy weapon damage plus {0}, and the target bleeds.",
          "Physical", [wdmg(140, 14, 20, 1.6), aura_on("cr_r1_thorn_bleed")], cd=6, melee=True, hint="Damage", prio=6),
        A("cr_r1_bramble_burst", "Bramble Burst", "nature", "Thornmaw shivers and every thorn on its back flies loose: {0} Nature damage to enemies within 15 yards, who bleed.",
          "Nature", [dmg(56, 70, 3.6), aura_on("cr_r1_thorn_bleed")], cast=3.0, cd=24, target="Self", area=circle(15),
          tags=["Telegraph"], hint="AoE", prio=9),
        A("cr_r1_grasping_roots", "Grasping Roots", "leaf", "Roots burst from the floor under someone at the back: rooted and crushed for Nature damage every 3 sec for 9 sec.",
          "Nature", [aura_on("cr_r1_grasping_roots")], cd=18, rng=40, hint="CC", prio=7),
        A("cr_r1_call_thornlings", "Call the Thornlings", "leaf", "Thornmaw shakes its bramble, and three thornlings tumble out of it.",
          "Nature", [summon("cr_r1_thornling", 3)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_call_thornlings_deep", "Call the Deep Thornlings", "leaf", "Wounded, Thornmaw tears up its own roots, and three more thornlings scramble free.",
          "Nature", [summon("cr_r1_thornling", 3)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_rootbound_frenzy", "Rootbound Frenzy", "rage", "Below a quarter of its health Thornmaw goes wild: damage dealt increased by 40%.",
          "Physical", [aura_on("cr_r1_rootbound_frenzy", "Self")], cd=600, target="Self", hint="Buff", prio=10, time="OffGcd"),
        # ---- the Weeping Twins
        A("cr_r1_sorrow_bolt", "Bolt of Sorrow", "frost", "A bolt of grief, cold as a well in winter: {0} Frost damage.", "Frost",
          [dmg(24, 30, 1.8)], cast=2.0, rng=30, hint="Damage", prio=3),
        A("cr_r1_drowning_sorrow", "Drowning Sorrow", "water_drop", "Sorrow weeps over a hero until they are wet through: {0} Frost damage, and their movement is slowed.",
          "Frost", [dmg(26, 32, 2.0), aura_on("cr_r1_drowning_sorrow")], cd=12, rng=30, hint="Damage", prio=6),
        A("cr_r1_veil_of_tears", "Veil of Tears", "water_drop", "Sorrow lifts her veil and the whole grove weeps: {0} Shadow damage to every enemy within 40 yards, and Shadow damage every 3 sec for 12 sec. Healers, be ready.",
          "Shadow", [dmg(16, 20, 1.0), aura_on("cr_r1_veil_of_tears")], cast=3.0, cd=24, target="Self", area=circle(40),
          tags=["Telegraph"], hint="AoE", prio=9),
        A("cr_r1_weeping_wisps", "Weeping Wisps", "water_drop", "Sorrow's tears rise from the pool as two weeping wisps.",
          "Frost", [summon("cr_r1_weeping_wisp", 2)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_grief_unbound", "Grief Unbound", "rage", "Below 30% health Sorrow lets go of everything at once: damage dealt increased by 50%.",
          "Shadow", [aura_on("cr_r1_grief_unbound", "Self")], cd=600, target="Self", hint="Buff", prio=10, time="OffGcd"),
        A("cr_r1_solace_mending", "Solace's Mending", "heal_plus", "Solace holds up her lantern and mends her sister: heals an ally for 10% of its health. Interrupt it.",
          "Holy", [{"type": "Heal", "min": 0, "max": 0, "pctOfMax": 10}], cast=3.0, cd=18, target="Ally", rng=40,
          tags=["Telegraph", "Heal"], hint="Heal", prio=9),
        A("cr_r1_lullaby", "Lullaby of Solace", "moon", "Solace hums an old grove lullaby to a hero: asleep for 12 sec. Any damage wakes them.",
          "Holy", [aura_on("cr_r1_lullaby")], cast=1.5, cd=18, rng=30, hint="CC", prio=7),
        A("cr_r1_dawn_lantern", "Dawn Lantern", "sun", "A small, warm, very determined light: {0} Holy damage.", "Holy",
          [dmg(20, 26, 1.6)], cast=2.0, rng=30, hint="Damage", prio=3),
        A("cr_r1_last_light", "The Last Light", "sun", "Below 30% health Solace burns her lantern down to the wick: damage and healing done increased by 50%.",
          "Holy", [aura_on("cr_r1_last_light", "Self")], cd=600, target="Self", hint="Buff", prio=10, time="OffGcd"),
        # ---- Mother Mire
        A("cr_r1_cauldron_bolt", "Bubbling Brew", "vial", "A ladle of boiling brew, flung with love: {0} Nature damage.", "Nature",
          [dmg(26, 34, 2.0)], cast=2.0, rng=30, hint="Damage", prio=3),
        A("cr_r1_bog_eruption", "Bog Eruption", "wave", "Mother Mire stirs the cauldron three times widdershins, and the whole bog boils over: {0} Nature damage to every enemy within 30 yards, and they scald for 9 sec.",
          "Nature", [dmg(40, 52, 2.8), aura_on("cr_r1_scalded")], cast=4.0, cd=24, target="Self", area=circle(30),
          tags=["Telegraph"], hint="AoE", prio=8),
        A("cr_r1_frog_hex", "Frog Hex", "curse", "Mother Mire points a crooked finger: a hero becomes a small, surprised frog for 15 sec. Any damage turns them back. Remove Curse ends it.",
          "Nature", [aura_on("cr_r1_frog_hex")], cast=1.5, cd=18, rng=30, hint="CC", prio=7),
        A("cr_r1_witchs_brew", "Witch's Brew", "potion_red", "Mother Mire takes a long swig from the cauldron: heals her for 8% of her health. Interrupt it.",
          "Nature", [{"type": "Heal", "min": 0, "max": 0, "pctOfMax": 8}], cast=3.0, cd=30, target="Self",
          tags=["Telegraph", "Heal"], hint="Heal", prio=9),
        A("cr_r1_call_mirespawn", "Something in the Pot", "skull", "Mother Mire tips the cauldron, and two mirespawn climb out of it.",
          "Nature", [summon("cr_r1_mirespawn", 2)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_call_mirespawn_deep", "The Bottom of the Pot", "skull", "Mother Mire scrapes the bottom of the cauldron, and two more mirespawn come up with the scrapings.",
          "Nature", [summon("cr_r1_mirespawn", 2)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_boiling_over", "Boiling Over", "rage", "Below 20% health Mother Mire loses her temper entirely: damage dealt increased by 50%.",
          "Nature", [aura_on("cr_r1_boiling_over", "Self")], cd=600, target="Self", hint="Buff", prio=10, time="OffGcd"),
        # ---- the Hollow Heart
        A("cr_r1_heartbeat", "Heartbeat", "heart", "BOOM. The heart beats, and the whole chamber beats with it: {0} Shadow damage to every enemy within 45 yards.",
          "Shadow", [dmg(20, 26, 1.4)], cd=12, target="Self", area=circle(45), hint="AoE", prio=7),
        A("cr_r1_grasping_lash", "Grasping Lash", "tentacle", "One of the heart's slender root-arms lashes out across the chamber: {0} damage, and the target is held fast for 6 sec.",
          "Physical", [dmg(40, 52, 2.6), aura_on("cr_r1_grasping_lash")], cd=6, rng=40, hint="Damage", prio=5),
        A("cr_r1_drink_the_light", "Drink the Light", "void", "The heart draws every light in the chamber into itself: {0} Shadow damage to every enemy within 45 yards, and healing taken is reduced by 30% for 15 sec. Interrupt it, or brace.",
          "Shadow", [dmg(64, 80, 4.2), aura_on("cr_r1_drained")], cast=6.0, cd=36, target="Self", area=circle(45),
          tags=["Telegraph"], hint="AoE", prio=8),
        A("cr_r1_sprout_seedlings", "Sprout", "leaf", "The heart beats, and two grey seedlings push up out of the roots.",
          "Shadow", [summon("cr_r1_hollow_seedling", 2)], cd=42, target="Self", hint="Summon", prio=9),
        A("cr_r1_hollowed_lights", "The Drowned Lights", "void", "At half its health the heart spits out two of the lights it swallowed, hollowed and furious.",
          "Shadow", [summon("cr_r1_hollowed_light", 2)], cd=600, target="Self", hint="Summon", prio=10),
        A("cr_r1_berserk", "Hollow Fury", "rage", "The fight has gone on too long: damage dealt increased by 200%.",
          "Shadow", [aura_on("cr_r1_berserk", "Self")], cd=600, target="Self", hint="Buff", prio=11, time="OffGcd"),
        A("cr_r1_hollow_fury", "Hollow Fury", "void", "Berserk, the boss sweeps the whole chamber every round: {0} Shadow damage to every enemy within 60 yards.",
          "Shadow", [dmg(120, 140, 6.0)], cd=6, target="Self", area=circle(60), hint="AoE", prio=11),
        A("cr_r1_final_beat", "The Last Beats", "rage", "Below 20% health the heart races: damage dealt increased by 50%.",
          "Shadow", [aura_on("cr_r1_final_beat", "Self")], cd=600, target="Self", hint="Buff", prio=10, time="OffGcd"),
    ]


def aura(aid, name, icon, kind, desc, *, school="Physical", dur=0.0, dispel="", states=None, mods=None, tick=0.0, ticks=None,
         absorb=None, stacks=0, brk=False):
    a = {"id": aid, "name": name, "icon": icon, "kind": kind, "school": school}
    if dispel:
        a["dispel"] = dispel
    a["duration"] = dur
    if stacks:
        a["maxStacks"] = stacks
    if states:
        a["states"] = states
    if brk:
        a["breakOnDamage"] = True
    if mods:
        a["mods"] = mods
    if absorb:
        a["absorb"] = absorb
    if tick:
        a["tickInterval"] = tick
        a["tickEffects"] = ticks
    a["description"] = desc
    return a


def pct(stat, v):
    return {"stat": stat, "value": v, "pct": True}


def auras():
    return [
        aura("cr_r1_sap_spores", "Sap Spores", "poison", "Debuff", "Nature damage every 3 sec.", school="Nature", dur=12,
             dispel="Poison", tick=3, ticks=[{"type": "Damage", "min": 6, "perLevel": 0.6}]),
        aura("cr_r1_hollow_howl", "Hollow Howl", "shout", "Buff", "Damage dealt increased by 15%.", school="Shadow", dur=18,
             mods=[pct("DamageDone", 15)]),
        aura("cr_r1_vigil_ward", "Vigil Ward", "shield", "Buff", "Absorbs damage.", dur=12,
             absorb={"amount": 220, "perLevel": 12}),
        aura("cr_r1_thorn_bleed", "Thorn Bleed", "blood", "Debuff", "Bleeding: Physical damage every 3 sec.", dur=9, stacks=3,
             tick=3, ticks=[{"type": "Damage", "min": 7, "perLevel": 0.6}]),
        aura("cr_r1_grasping_roots", "Grasping Roots", "leaf", "Debuff", "Rooted. Nature damage every 3 sec.", school="Nature", dur=9,
             dispel="Magic", states=["Root"], tick=3, ticks=[{"type": "Damage", "min": 10, "perLevel": 0.8}]),
        aura("cr_r1_rootbound_frenzy", "Rootbound Frenzy", "rage", "Buff", "Damage dealt increased by 40%.", mods=[pct("DamageDone", 40)]),
        aura("cr_r1_drowning_sorrow", "Drowning Sorrow", "water_drop", "Debuff", "Soaked through: movement speed reduced by 40%.",
             school="Frost", dur=9, dispel="Magic", mods=[pct("MoveSpeed", -40)]),
        aura("cr_r1_veil_of_tears", "Veil of Tears", "water_drop", "Debuff", "Weeping: Shadow damage every 3 sec.", school="Shadow",
             dur=12, dispel="Magic", tick=3, ticks=[{"type": "Damage", "min": 6, "perLevel": 0.45}]),
        aura("cr_r1_grief_unbound", "Grief Unbound", "rage", "Buff", "Damage dealt increased by 50%.", school="Shadow",
             mods=[pct("DamageDone", 50)]),
        aura("cr_r1_lullaby", "Lullaby of Solace", "moon", "Debuff", "Asleep. Any damage will wake the target.", school="Holy",
             dur=12, dispel="Magic", states=["Sleep"], brk=True),
        aura("cr_r1_last_light", "The Last Light", "sun", "Buff", "Damage and healing done increased by 50%.", school="Holy",
             mods=[pct("DamageDone", 50), pct("HealingDone", 50)]),
        aura("cr_r1_scalded", "Scalded", "fire", "Debuff", "Nature damage every 3 sec.", school="Nature", dur=9,
             tick=3, ticks=[{"type": "Damage", "min": 5, "perLevel": 0.35}]),
        aura("cr_r1_frog_hex", "Frog Hex", "curse", "Debuff", "A small, surprised frog. Any damage breaks the hex.", school="Nature",
             dur=15, dispel="Curse", states=["Polymorph"], brk=True),
        aura("cr_r1_boiling_over", "Boiling Over", "rage", "Buff", "Damage dealt increased by 50%.", school="Nature",
             mods=[pct("DamageDone", 50)]),
        aura("cr_r1_grasping_lash", "Grasping Lash", "tentacle", "Debuff", "Held fast by a root-arm: cannot move.", dur=6,
             states=["Root"]),
        aura("cr_r1_drained", "Drained", "void", "Debuff", "Healing taken reduced by 30%.", school="Shadow", dur=15,
             mods=[pct("HealingTaken", -30)]),
        aura("cr_r1_berserk", "Hollow Fury", "rage", "Buff", "Damage dealt increased by 200%.", school="Shadow",
             mods=[pct("DamageDone", 200)]),
        aura("cr_r1_final_beat", "The Last Beats", "rage", "Buff", "Damage dealt increased by 50%.", school="Shadow",
             mods=[pct("DamageDone", 50)]),
    ]


def bundle():
    return {"_note": "The Hollow Heart raid's creatures (Docs/Expansion.md §8 row r1): Elite trash, adds and four bosses. "
                     "Generated by Tools/datagen/r1/gen_r1.py; health tuned for a raid of ten at level 22 (TestsContentR1). Owner: r1.",
            "creatures": creatures(), "abilities": abilities(), "auras": auras()}
