"""items.json: consumables (+ their use abilities/auras), quest items, junk, reagents, equipment, suffixes."""
from wn_common import *

ITEMS, ABIL, AURAS = [], [], []
ORDER = ["id", "name", "icon", "description", "kind", "quality", "itemLevel", "requiredLevel", "equip", "armorType",
         "weaponType", "armor", "block", "minDamage", "maxDamage", "speed", "damageSchool", "stats", "equipEffects",
         "use", "consumable", "stack", "price", "classes", "unique", "quest", "art"]


def item(**kw):
    assert set(kw) <= set(ORDER), set(kw) - set(ORDER)
    assert not any(i["id"] == kw["id"] for i in ITEMS), kw["id"]
    ITEMS.append({k: kw[k] for k in ORDER if k in kw and kw[k] is not None})
    return kw["id"]


# ------------------------------------------------------------------ formulas
TYPE_BASE = {"Cloth": (1.2, 6), "Leather": (2.6, 12), "Mail": (5.0, 30), "Plate": (9.0, 60)}
SLOT_ARMOR = {"Chest": 1.0, "Legs": 0.875, "Head": 0.8125, "Shoulder": 0.75, "Feet": 0.6875, "Hands": 0.625,
              "Waist": 0.5625, "Wrist": 0.4375, "Back": 0.65}
Q_ARMOR = {"Poor": 0.9, "Common": 1.0, "Uncommon": 1.1, "Rare": 1.2, "Epic": 1.3}
Q_PRICE = {"Poor": 0.5, "Common": 1.0, "Uncommon": 2.5, "Rare": 6.0, "Epic": 15.0}
SLOT_PRICE = {"Chest": 1.0, "Legs": 1.0, "Head": 0.85, "Shoulder": 0.8, "Feet": 0.75, "Hands": 0.65, "Waist": 0.6,
              "Wrist": 0.5, "Back": 0.6, "Finger": 0.9, "Neck": 0.9, "Trinket": 1.2, "OneHand": 1.1, "MainHand": 1.1,
              "OffHand": 0.8, "TwoHand": 1.5, "Ranged": 1.1}
SLOT_ICON = {"Chest": "armor", "Legs": "armor", "Shoulder": "armor", "Wrist": "armor", "Waist": "armor",
             "Hands": "hand", "Feet": "boot", "Back": "wings", "Finger": "halo", "Neck": "heart", "Trinket": "star"}


def price(ilvl, slot, q, mult=1.0):
    p = 1.2 * ilvl ** 2.2 * SLOT_PRICE[slot] * Q_PRICE[q] * mult
    return max(5, int(round(p / 5.0)) * 5)


def armor_val(atype, slot, ilvl, q):
    if slot == "Back":
        atype = "Cloth"
    a, b = TYPE_BASE[atype]
    return int(round((a * ilvl + b) * SLOT_ARMOR[slot] * Q_ARMOR[q]))


def armor(id, name, atype, slot, ilvl, req, q="Common", stats=None, desc=None, icon=None, effects=None, unique=None):
    if icon is None:
        icon = SLOT_ICON.get(slot, "armor")
        if slot == "Head":
            icon = "mask" if atype in ("Cloth", "Leather") else "crown"
    return item(id=id, name=name, icon=icon, description=desc, kind="Armor", quality=q, itemLevel=ilvl,
                requiredLevel=req, equip=slot, armorType=("Cloth" if slot == "Back" else atype),
                armor=armor_val(atype, slot, ilvl, q), stats=stats, equipEffects=effects, price=price(ilvl, slot, q),
                unique=unique)


WSPEC = {  # weaponType: (equip, dps multiplier, icon)
    "Dagger": ("OneHand", 1.0, "dagger"), "FistWeapon": ("OneHand", 1.0, "fist"),
    "OneHandAxe": ("OneHand", 1.0, "axe"), "OneHandMace": ("OneHand", 1.0, "mace"),
    "OneHandSword": ("OneHand", 1.0, "sword"), "Polearm": ("TwoHand", 1.3, "spear"),
    "Staff": ("TwoHand", 1.3, "staff"), "TwoHandAxe": ("TwoHand", 1.3, "axe"),
    "TwoHandMace": ("TwoHand", 1.3, "hammer"), "TwoHandSword": ("TwoHand", 1.3, "sword"),
    "Bow": ("Ranged", 0.95, "bow"), "Crossbow": ("Ranged", 0.95, "bow"), "Gun": ("Ranged", 0.95, "gun"),
    "Thrown": ("Ranged", 0.9, "daggers"), "Wand": ("Ranged", 1.0, "sparkle"),
}
DPS = {"Poor": (0.25, 0.5), "Common": (0.33, 0.9), "Uncommon": (0.45, 1.4), "Rare": (0.55, 1.6), "Epic": (0.66, 2.0)}
WAND_Q = {"Poor": 0.8, "Common": 1.0, "Uncommon": 1.15, "Rare": 1.3, "Epic": 1.5}


def weapon(id, name, wtype, ilvl, req, speed, q="Common", stats=None, desc=None, equip=None, school=None,
           effects=None, icon=None, unique=None):
    eq, mult, ic = WSPEC[wtype]
    if wtype == "Wand":
        dps = (0.75 * ilvl + 1.0) * WAND_Q[q]
    else:
        a, b = DPS[q]
        dps = (a * ilvl + b) * mult
    avg = dps * speed
    mn = max(1, int(round(avg * 0.72)))
    mx = max(mn + 1, int(round(avg * 1.28)))
    slot = equip or eq
    return item(id=id, name=name, icon=icon or ic, description=desc, kind="Weapon", quality=q, itemLevel=ilvl,
                requiredLevel=req, equip=slot, weaponType=wtype, minDamage=mn, maxDamage=mx, speed=speed,
                damageSchool=school, stats=stats, equipEffects=effects,
                price=price(ilvl, slot, q, 1.2), unique=unique)


def shield(id, name, ilvl, req, q="Common", stats=None, desc=None, unique=None):
    return item(id=id, name=name, icon="shield", description=desc, kind="Weapon", quality=q, itemLevel=ilvl,
                requiredLevel=req, equip="OffHand", weaponType="Shield",
                armor=int(round((22 * ilvl + 20) * Q_ARMOR[q])), block=int(round(0.45 * ilvl + 1)),
                stats=stats, price=price(ilvl, "OffHand", q, 1.1), unique=unique)


def held(id, name, ilvl, req, q, stats, desc, icon="arcane_orb", unique=None):
    return item(id=id, name=name, icon=icon, description=desc, kind="Weapon", quality=q, itemLevel=ilvl,
                requiredLevel=req, equip="OffHand", weaponType="HeldInOffhand", stats=stats,
                price=price(ilvl, "OffHand", q), unique=unique)


def accessory(id, name, slot, ilvl, req, q, stats, desc, icon=None, effects=None, unique=None, use=None):
    if slot == "Back":  # cloaks are cloth armour
        return item(id=id, name=name, icon=icon or SLOT_ICON[slot], description=desc, kind="Armor", quality=q,
                    itemLevel=ilvl, requiredLevel=req, equip=slot, armorType="Cloth",
                    armor=armor_val("Cloth", "Back", ilvl, q), stats=stats, equipEffects=effects, use=use,
                    price=price(ilvl, slot, q), unique=unique)
    return item(id=id, name=name, icon=icon or SLOT_ICON[slot], description=desc, kind="Accessory", quality=q,
                itemLevel=ilvl, requiredLevel=req, equip=slot, stats=stats, equipEffects=effects, use=use,
                price=price(ilvl, slot, q), unique=unique)


# ------------------------------------------------------- use abilities / auras

def use_ability(id, name, icon, desc, effects, target="Self", time="OffGcd", cooldown=0, group="", range_=0,
                requires=None, school="Physical"):
    a = {"id": id, "name": name, "icon": icon, "description": desc, "school": school, "time": time,
         "target": target, "hidden": True, "breaksStealth": True}
    if cooldown: a["cooldown"] = cooldown
    if group: a["cooldownGroup"] = group
    if range_: a["range"] = range_
    if requires: a["requires"] = requires
    a["effects"] = effects
    a["aiHint"] = "Heal" if any(e["type"] == "Heal" for e in effects) else "Buff"
    ABIL.append(a)
    return id


def aura(**kw):
    AURAS.append(kw)
    return kw["id"]


# ===================================================================== consumables
def potion(id, name, req, ilvl, price_, desc, effects, icon, use_desc):
    u = use_ability("use_" + id, name, icon, use_desc, effects, cooldown=120, group="potion")
    item(id=id, name=name, icon=icon, description=desc, kind="Consumable", quality="Common", itemLevel=ilvl,
         requiredLevel=req, use=u, consumable=True, stack=5, price=price_)


potion("potion_minor_healing", "Minor Healing Potion", 1, 5, 25, "Tastes faintly of peppermint and pennies.",
       [{"type": "Heal", "min": 70, "max": 90, "target": "Self"}], "potion_red", "Restores {0} health.")
potion("potion_lesser_healing", "Lesser Healing Potion", 3, 13, 100, "A ruby-red draught brewed with camphor leaf.",
       [{"type": "Heal", "min": 140, "max": 180, "target": "Self"}], "potion_red", "Restores {0} health.")
potion("potion_healing", "Healing Potion", 12, 22, 400, "Warm as a hearth. The cork glows faintly.",
       [{"type": "Heal", "min": 280, "max": 360, "target": "Self"}], "potion_red", "Restores {0} health.")
potion("potion_minor_mana", "Minor Mana Potion", 5, 10, 80, "Fizzy, blue, and tastes like a thunderstorm smells.",
       [{"type": "GainResource", "resource": "Mana", "amount": 160, "target": "Self"}], "potion_blue",
       "Restores {0} mana.")
potion("potion_lesser_mana", "Lesser Mana Potion", 14, 24, 300, "Swirls on its own when nobody is looking.",
       [{"type": "GainResource", "resource": "Mana", "amount": 320, "target": "Self"}], "potion_blue",
       "Restores {0} mana.")
potion("potion_minor_rejuvenation", "Minor Rejuvenation Potion", 5, 10, 120,
       "Half red, half blue, and stubbornly refuses to mix.",
       [{"type": "Heal", "min": 90, "max": 150, "target": "Self"},
        {"type": "GainResource", "resource": "Mana", "amount": 120, "target": "Self"}], "potion_red",
       "Restores {0} health and {1} mana.")
potion("potion_rage", "Rage Potion", 4, 10, 100, "Brewed from boar's blood and bad tempers.",
       [{"type": "GainResource", "resource": "Rage", "amount": 30, "target": "Self"}], "rage",
       "Increases rage by {0}.")


def food(id, name, req, ilvl, price_, desc, per_tick, ticks, kind="Food", icon="food", extra_expire=None,
         well_fed_desc=""):
    is_food = kind == "Food"
    aid = ("cn_food_" if is_food else "cn_drink_") + id.split("_", 1)[1]
    total = per_tick * ticks
    eff = ({"type": "Heal", "min": per_tick, "target": "Target"} if is_food else
           {"type": "GainResource", "resource": "Mana", "amount": per_tick, "target": "Target"})
    a = {"id": aid, "name": "Food" if is_food else "Drink", "icon": icon,
         "description": (f"Restoring {total} health over {ticks * 3} sec." if is_food else
                         f"Restoring {total} mana over {ticks * 3} sec.") + well_fed_desc,
         "kind": "Buff", "duration": ticks * 3, "tickInterval": 3, "exclusiveGroup": "food" if is_food else "drink",
         "tags": ["Food" if is_food else "Drink"], "breakOnDamage": True, "tickEffects": [eff]}
    if extra_expire:
        a["onExpire"] = extra_expire
    aura(**a)
    verb = "eat" if is_food else "drink"
    u = use_ability("use_" + id, name, icon,
                    (f"Restores {total} health over {ticks * 3} sec. Must remain seated while eating."
                     if is_food else f"Restores {total} mana over {ticks * 3} sec. Must remain seated while drinking.")
                    + well_fed_desc,
                    [{"type": "ApplyAura", "aura": aid, "target": "Self"}], time="Gcd",
                    requires={"notInCombat": True})
    item(id=id, name=name, icon=icon, description=desc, kind=kind, quality="Common", itemLevel=ilvl,
         requiredLevel=req, use=u, consumable=True, stack=20, price=price_)


aura(id="cn_well_fed", name="Well Fed", icon="food", kind="Buff", duration=900, exclusiveGroup="well_fed",
     description="Stamina and Spirit increased by 4 for 15 min.", mods=st(Stamina=4, Spirit=4))
food("food_rice_ball", "Honeyed Rice Ball", 1, 5, 25, "Wrapped in a camphor leaf by someone who cared.", 10, 6)
food("food_honey_cake", "Honey Cake", 5, 10, 125,
     "Small spirits are said to find these irresistible. So are large adventurers.", 35, 7)
food("food_mushroom_skewer", "Glowcap Skewer", 5, 10, 125,
     "Grilled forest mushrooms. They stop glowing after the second bite, mostly.", 35, 7)
food("food_lantern_stew", "Sleepy Lantern Stew", 15, 20, 400,
     "Dorrit's own recipe. Nobody has ever finished a bowl and then wanted to fight anything.", 69, 8,
     extra_expire=[{"type": "ApplyAura", "aura": "cn_well_fed", "target": "Target"}],
     well_fed_desc=" If you spend the full time eating, you become Well Fed (+4 Stamina and Spirit for 15 min).")
food("drink_spring_water", "Lanternvale Spring Water", 1, 5, 25, "Cold enough to make your teeth sing.", 25, 6,
     kind="Drink", icon="drink")
food("drink_barley_tea", "Roasted Barley Tea", 5, 10, 125, "Nutty, warm and served in a chipped cup.", 62, 7,
     kind="Drink", icon="drink")
food("drink_melon_juice", "Cold Melon Juice", 15, 20, 400, "Pressed from Bram's prize melons. He will mention this.",
     104, 8, kind="Drink", icon="drink")

# bandages
for bid, name, req, ilvl, pr, per, ticks, desc in [
    ("bandage_linen", "Linen Bandage", 1, 5, 40, 11, 6, "Clean, if not exactly new."),
    ("bandage_wool", "Wool Bandage", 8, 15, 120, 16, 7, "Thick and itchy, but it holds."),
]:
    aid = "cn_" + bid
    aura(id=aid, name="First Aid", icon="bandage", kind="Buff", duration=ticks, tickInterval=1,
         description=f"Heals {per * ticks} damage over {ticks} sec. Interrupted by damage.",
         breakOnDamage=True, tickEffects=[{"type": "Heal", "min": per, "target": "Target"}])
    u = use_ability("use_" + bid, name, "bandage", f"Heals {per * ticks} damage over {ticks} sec.",
                    [{"type": "ApplyAura", "aura": aid}], target="Ally", time="Gcd", cooldown=60, group="bandage",
                    range_=5)
    item(id=bid, name=name, icon="bandage", description=desc, kind="Consumable", quality="Common", itemLevel=ilvl,
         requiredLevel=req, use=u, consumable=True, stack=20, price=pr)


# elixirs & scrolls
def buff_consumable(id, name, icon, req, ilvl, pr, desc, mods, dur, aura_desc, target="Self", time="Gcd",
                    group="", q="Common", stack=5, aura_name=None):
    aid = "cn_" + id
    a = dict(id=aid, name=aura_name or name, icon=icon, kind="Buff", duration=dur, description=aura_desc, mods=mods)
    if group:
        a["exclusiveGroup"] = group
    aura(**a)
    u = use_ability("use_" + id, name, icon, aura_desc, [{"type": "ApplyAura", "aura": aid}], target=target,
                    time=time, range_=30 if target == "Ally" else 0)
    item(id=id, name=name, icon=icon, description=desc, kind="Consumable", quality=q, itemLevel=ilvl,
         requiredLevel=req, use=u, consumable=True, stack=stack, price=pr)


buff_consumable("elixir_lions_strength", "Elixir of Lion's Strength", "vial", 1, 5, 60,
                "Smells like wet fur and courage.", st(Strength=4), 3600, "Strength increased by 4 for 1 hour.")
buff_consumable("elixir_minor_agility", "Elixir of Minor Agility", "vial", 2, 7, 80,
                "Your fingers tingle before you've even uncorked it.", st(Agility=4), 3600,
                "Agility increased by 4 for 1 hour.")
buff_consumable("elixir_minor_fortitude", "Elixir of Minor Fortitude", "vial", 2, 7, 80,
                "Thick, earthy and good for you, as all terrible things are.", st(Health=27), 3600,
                "Maximum health increased by 27 for 1 hour.")
buff_consumable("elixir_minor_defense", "Elixir of Minor Defense", "vial", 1, 5, 50,
                "Leaves a faint stony aftertaste.", st(Armor=50), 3600, "Armor increased by 50 for 1 hour.")
buff_consumable("elixir_wisdom", "Elixir of Wisdom", "vial", 10, 18, 150,
                "Brewed under a full moon by someone who insisted that mattered.", st(Intellect=6), 3600,
                "Intellect increased by 6 for 1 hour.")
buff_consumable("elixir_lantern_oil", "Lantern-Oil Tonic", "fire", 6, 12, 200,
                "Tilly swears by it against 'the grey'. It is mostly lamp oil, honey and hope.",
                [{"stat": "Resistance", "value": 15, "school": "Shadow"}], 3600,
                "Shadow resistance increased by 15 for 1 hour.")
buff_consumable("whetstone_garrow", "Garrow's Whetstone", "rock", 1, 8, 60,
                "A palm-worn stone. Garrow insists it 'knows what it's doing'.", st(AttackPower=8), 1800,
                "Attack power increased by 8 for 30 min.", aura_name="Sharpened Weapon")

for sid, nm, mods, d, req, pr in [
    ("scroll_stamina", "Scroll of Stamina", st(Stamina=3), "Stamina increased by 3 for 30 min.", 1, 60),
    ("scroll_strength", "Scroll of Strength", st(Strength=3), "Strength increased by 3 for 30 min.", 1, 60),
    ("scroll_agility", "Scroll of Agility", st(Agility=3), "Agility increased by 3 for 30 min.", 1, 60),
    ("scroll_intellect", "Scroll of Intellect", st(Intellect=2), "Intellect increased by 2 for 30 min.", 1, 60),
    ("scroll_spirit", "Scroll of Spirit", st(Spirit=3), "Spirit increased by 3 for 30 min.", 1, 60),
    ("scroll_protection", "Scroll of Protection", st(Armor=60), "Armor increased by 60 for 30 min.", 1, 60),
]:
    buff_consumable(sid, nm, "feather", req, 5, pr, "The ink shifts when you read it aloud.", mods, 1800, d,
                    target="Ally", group=sid, stack=5)

# ======================================================================= reagents
for rid, nm, ic, pr, desc in [
    ("reagent_light_feather", "Light Feather", "feather", 15, "A downy feather that never quite settles."),
    ("reagent_ankh", "Ankh", "cross", 200, "A small totem of carved bone, warm to the touch."),
    ("reagent_flash_powder", "Flash Powder", "sparkle", 25, "Do not sneeze near it."),
    ("reagent_holy_candle", "Holy Candle", "fire", 125, "Blessed beeswax from the Lanternvale shrine."),
    ("reagent_arcane_powder", "Arcane Powder", "arcane", 100, "Glitters in a way that makes cats nervous."),
]:
    item(id=rid, name=nm, icon=ic, description=desc, kind="Reagent", quality="Common", itemLevel=1, stack=20,
         price=pr)

# ===================================================================== quest items
for qid, nm, ic, q, desc, stack in [
    ("kindling_taper", "Kindling Taper", "fire", "mq_lanterns",
     "A long beeswax taper lit from every hearth in Lanternvale. Its small flame never quite goes out, though it "
     "never quite grows either.", 1),
    ("spirit_ember", "Spirit Ember", "ember", "mq_lanterns",
     "A warm mote of light rescued from a Hollow wisp. It hums, very faintly, like a cat that is trying not to.",
     10),
    ("lantern_wick", "Lantern Wick", "fire", "sq_wicks",
     "A braided spirit-wick, slightly nibbled. Mossling teeth marks, by the look of it.", 20),
    ("fennicks_satchel", "Fennick's Satchel", "coin", "sq_satchel",
     "A battered leather mail satchel, sticky with spider silk. The letters inside are mostly dry.", 1),
    ("ishiro_letter", "Keeper Ishiro's Letter", "feather", "sq_satchel",
     "Sealed with grey wax and addressed to Elder Maru in a careful, shaking hand. It is three weeks late.", 1),
    ("nells_bell", "Nell's Spirit Bell", "sparkle", "sq_spirit_friend",
     "A tiny brass bell on a red string. Nell says Moppet always comes when it rings. Almost always.", 1),
]:
    item(id=qid, name=nm, icon=ic, description=desc, kind="Quest", quality="Common", itemLevel=1, stack=stack,
         price=0, quest=q, unique=True)

# ============================================================================ junk
for jid, nm, ic, sell, desc in [
    ("junk_broken_fang", "Broken Wolf Fang", "fang", 6, ""),
    ("junk_ruined_pelt", "Ruined Pelt", "paw", 7, ""),
    ("junk_cracked_tusk", "Cracked Boar Tusk", "boar", 9, ""),
    ("junk_bristly_hide", "Scrap of Bristly Hide", "paw", 8, ""),
    ("junk_sticky_web", "Sticky Web Strand", "spider", 10, "It is still trying to stick to something."),
    ("junk_chitin_fragment", "Chitin Fragment", "spider", 12, ""),
    ("junk_tiny_leaf_hat", "Tiny Leaf Hat", "leaf", 15, "Far too small for you. You try it on anyway."),
    ("junk_damp_pebbles", "Collection of Damp Pebbles", "rock", 11, "Lovingly sorted by size and shininess."),
    ("junk_nibbled_mushroom", "Half-Nibbled Mushroom", "leaf", 5, ""),
    ("junk_torn_scarf_mask", "Torn Scarf Mask", "mask", 22, ""),
    ("junk_chipped_dice", "Chipped Dice", "coin", 30, "Both of them land on six. Suspicious."),
    ("junk_dented_cup", "Dented Tin Cup", "drink", 18, ""),
    ("junk_bad_poetry", "Bandit's Bad Poetry", "feather", 1,
     "'O toll, O toll, thou pays't thy toll / or else we taketh thy... goll.' It goes on like this."),
    ("junk_grey_ash", "Pinch of Grey Ash", "void", 25, "It leaves no smudge on your fingers. That's worse, somehow."),
    ("junk_dull_lantern_glass", "Dull Lantern Glass", "sparkle", 35, ""),
    ("junk_faded_prayer_strip", "Faded Prayer Strip", "feather", 28, "Whatever was written here has been emptied out."),
    ("junk_hollow_shard", "Hollow Shard", "void", 45, ""),
    ("junk_blighted_bark", "Blighted Bark", "leaf", 40, ""),
    ("junk_shiny_button", "Shiny Button", "coin", 50, "Pip would like this."),
    ("junk_bent_spoon", "Bent Spoon", "coin", 3, ""),
]:
    item(id=jid, name=nm, icon=ic, description=desc or None, kind="Junk", quality="Poor", itemLevel=1, stack=10,
         price=sell * 4)

# ===================================================================== common armour (vendor)
SETS = {
    "Cloth": [("homespun", "Homespun", 5, 1, {"Chest": "Robe", "Legs": "Leggings", "Feet": "Slippers",
                                              "Hands": "Gloves", "Waist": "Sash", "Wrist": "Cuffs"}),
              ("lampwool", "Lampwool", 12, 7, {"Chest": "Robe", "Legs": "Trousers", "Feet": "Shoes", "Hands": "Mitts",
                                              "Waist": "Cord", "Wrist": "Bracelets", "Head": "Hood",
                                              "Shoulder": "Mantle"})],
    "Leather": [("tanned", "Tanned Leather", 5, 1, {"Chest": "Vest", "Legs": "Pants", "Feet": "Boots",
                                                    "Hands": "Gloves", "Waist": "Belt", "Wrist": "Bracers"}),
                ("burnished", "Burnished Leather", 12, 7, {"Chest": "Jerkin", "Legs": "Leggings", "Feet": "Boots",
                                                           "Hands": "Gloves", "Waist": "Belt", "Wrist": "Bracers",
                                                           "Head": "Cap", "Shoulder": "Shoulderpads"})],
    "Mail": [("ringlink", "Ringlink", 5, 1, {"Chest": "Hauberk", "Legs": "Leggings", "Feet": "Boots",
                                            "Hands": "Gauntlets", "Waist": "Girdle", "Wrist": "Bracers"}),
             ("riveted", "Riveted Chain", 12, 7, {"Chest": "Hauberk", "Legs": "Leggings", "Feet": "Boots",
                                                  "Hands": "Gauntlets", "Waist": "Belt", "Wrist": "Bracers",
                                                  "Head": "Coif", "Shoulder": "Pauldrons"})],
    "Plate": [("lanternguard", "Lanternguard", 15, 10, {"Chest": "Breastplate", "Legs": "Legplates",
                                                       "Feet": "Sabatons", "Hands": "Gauntlets", "Waist": "Girdle",
                                                       "Wrist": "Vambraces", "Head": "Helm",
                                                       "Shoulder": "Pauldrons"})],
}
VENDOR_ARMOR = []
for atype, sets in SETS.items():
    for key, prefix, ilvl, req, slots in sets:
        for slot, word in slots.items():
            iid = f"{key}_{word.lower()}"
            armor(iid, f"{prefix} {word}", atype, slot, ilvl, req,
                  desc=("Heavy ceremonial plate from the old Lantern Guard. Only seasoned knights have the "
                        "training to wear it." if atype == "Plate" and slot == "Chest" else None))
            VENDOR_ARMOR.append(iid)
VENDOR_ARMOR.append(armor("wool_cloak", "Wool Cloak", "Cloth", "Back", 5, 1))
VENDOR_ARMOR.append(armor("travelers_cloak", "Traveler's Cloak", "Cloth", "Back", 12, 7))

# ===================================================================== common weapons (vendor)
VENDOR_WEAPONS = []
for wid, nm, wt, ilvl, req, spd in [
    ("bone_handled_knife", "Bone-Handled Knife", "Dagger", 5, 1, 1.7),
    ("knuckle_wraps", "Leather Knuckle-Wraps", "FistWeapon", 5, 1, 1.6),
    ("woodcutters_hatchet", "Woodcutter's Hatchet", "OneHandAxe", 5, 1, 2.5),
    ("oak_cudgel", "Oak Cudgel", "OneHandMace", 5, 1, 2.4),
    ("militia_shortsword", "Militia Shortsword", "OneHandSword", 5, 1, 2.2),
    ("herdsmans_spear", "Herdsman's Spear", "Polearm", 5, 1, 3.2),
    ("walking_staff", "Walking Staff", "Staff", 5, 1, 3.0),
    ("felling_axe", "Felling Axe", "TwoHandAxe", 5, 1, 3.3),
    ("fencepost_maul", "Fencepost Maul", "TwoHandMace", 5, 1, 3.5),
    ("rust_spotted_claymore", "Rust-Spotted Claymore", "TwoHandSword", 5, 1, 3.2),
    ("hunting_shortbow", "Hunting Shortbow", "Bow", 5, 1, 2.4),
    ("light_crossbow", "Light Crossbow", "Crossbow", 5, 1, 3.0),
    ("tinkers_pipe_gun", "Tinker's Pipe-Gun", "Gun", 5, 1, 2.6),
    ("throwing_knives", "Throwing Knives", "Thrown", 5, 1, 2.0),
    ("steel_dirk", "Steel Dirk", "Dagger", 12, 7, 1.8),
    ("iron_knuckles", "Iron Knuckles", "FistWeapon", 12, 7, 1.7),
    ("bearded_axe", "Bearded Axe", "OneHandAxe", 12, 7, 2.6),
    ("flanged_mace", "Flanged Mace", "OneHandMace", 12, 7, 2.5),
    ("broadsword", "Broadsword", "OneHandSword", 12, 7, 2.4),
    ("ash_glaive", "Ash Glaive", "Polearm", 12, 7, 3.3),
    ("ironshod_quarterstaff", "Ironshod Quarterstaff", "Staff", 12, 7, 3.1),
    ("double_bit_axe", "Double-Bit Axe", "TwoHandAxe", 12, 7, 3.4),
    ("war_maul", "War Maul", "TwoHandMace", 12, 7, 3.6),
    ("longblade", "Longblade", "TwoHandSword", 12, 7, 3.2),
    ("recurve_bow", "Recurve Bow", "Bow", 12, 7, 2.6),
    ("heavy_crossbow", "Heavy Crossbow", "Crossbow", 12, 7, 3.1),
    ("bronze_musket", "Bronze Musket", "Gun", 12, 7, 2.7),
    ("throwing_axes", "Balanced Throwing Axes", "Thrown", 12, 7, 2.1),
]:
    VENDOR_WEAPONS.append(weapon(wid, nm, wt, ilvl, req, spd))
VENDOR_WEAPONS.append(shield("wooden_buckler", "Wooden Buckler", 5, 1))
VENDOR_WEAPONS.append(shield("round_oak_shield", "Round Oak Shield", 12, 7))

VENDOR_WANDS = [
    weapon("willow_wand", "Willow Wand", "Wand", 6, 3, 1.5, school="Arcane",
           desc="Snapped from the old willow by the well. It didn't mind."),
    weapon("ember_twig_wand", "Ember Twig Wand", "Wand", 9, 5, 1.6, school="Fire",
           desc="Smells of bonfire night."),
    weapon("frost_etched_wand", "Frost-Etched Wand", "Wand", 12, 8, 1.7, school="Frost",
           desc="Always cold, even in a pocket."),
    weapon("gloom_wand", "Gloom Wand", "Wand", 12, 8, 1.6, school="Shadow",
           desc="Tilly keeps it at the back of the stall, wrapped in a tea towel."),
]

# ========================================================================= greens
G = "Uncommon"
GREENS = {}


def g(kind, *a, **k):
    iid = {"armor": armor, "weapon": weapon, "shield": shield, "held": held, "acc": accessory}[kind](*a, **k)
    GREENS[iid] = True
    return iid


# cloth
g("armor", "pilgrims_cowl", "Pilgrim's Cowl", "Cloth", "Head", 10, 5, G, st(Intellect=3, Spirit=3),
  "Stitched with the names of shrines, each one crossed out with a little lantern.")
g("armor", "lamplighters_gloves", "Lamplighter's Gloves", "Cloth", "Hands", 9, 4, G, st(Intellect=2, Spirit=2),
  "Singed at every fingertip. Tobben says that's how you know they work.")
g("armor", "mossweave_sandals", "Mossweave Sandals", "Cloth", "Feet", 8, 3, G, st(Spirit=2, Stamina=1),
  "Woven by Mosslings, who were very proud and only slightly confused about feet.")
g("armor", "robe_of_the_ember_wick", "Robe of the Ember Wick", "Cloth", "Chest", 12, 7, G,
  st(("SpellDamage", 3, "Fire"), Intellect=4, Stamina=2), "The hem smoulders politely and never catches.")
g("armor", "hexers_charm_robe", "Hexer's Charm-Hung Robe", "Cloth", "Chest", 11, 6, G,
  st(("SpellDamage", 3, "Shadow"), Intellect=3, Stamina=2), "Every little charm jingles a different curse.")
g("armor", "bindings_of_quiet_dusk", "Bindings of Quiet Dusk", "Cloth", "Wrist", 13, 8, G, st(Intellect=2, Spirit=2),
  "They make the moment after sunset last a little longer.")
g("armor", "starlit_trousers", "Starlit Trousers", "Cloth", "Legs", 14, 9, G, st(Intellect=4, Spirit=3, Stamina=2),
  "Embroidered with constellations nobody in the valley has a name for.")
# leather
g("armor", "bristleback_jerkin", "Bristleback Jerkin", "Leather", "Chest", 8, 3, G, st(Agility=3, Stamina=2),
  "Still bristly. Wear a shirt underneath.")
g("armor", "wolfrunner_boots", "Wolfrunner Boots", "Leather", "Feet", 9, 4, G, st(Agility=2, Stamina=2),
  "Soft-soled and silent. The sheep never heard them coming either.")
g("armor", "cutthroats_hood", "Cutthroat's Hood", "Leather", "Head", 11, 6, G, st(Agility=3, Strength=2),
  "Comes with a matching scowl, sold separately.")
g("armor", "spiderhide_bracers", "Spiderhide Bracers", "Leather", "Wrist", 8, 3, G, st(Agility=2, Stamina=1),
  "Faintly tacky. Excellent for catching thrown things.")
g("armor", "shepherds_fleece_vest", "Shepherd's Fleece Vest", "Leather", "Chest", 7, 2, G, st(Stamina=3, Spirit=2),
  "Bram's spare. It smells of hay, rain and a very loyal dog.")
g("armor", "whisperwood_stalker_leggings", "Whisperwood Stalker Leggings", "Leather", "Legs", 13, 8, G,
  st(Agility=4, Stamina=3), "Patched with moss in all the places that matter.")
g("armor", "puddlecaps_mushroom_hat", "Puddlecap's Mushroom Hat", "Leather", "Head", 9, 4, G,
  st(Spirit=3, Intellect=2, Stamina=1),
  "A gift of Mossling friendship, enlarged with great ceremony and a lot of string.")
g("armor", "cogwright_goggles", "Cogwright Goggles", "Leather", "Head", 6, 1, G, st(Agility=2, Intellect=1),
  "Three lenses, two straps, one very important sticker.")
# mail
g("armor", "bridgewardens_hauberk", "Bridgewarden's Hauberk", "Mail", "Chest", 12, 7, G, st(Strength=4, Stamina=3),
  "Issued to the Old Bridge's last proper warden, who retired to keep bees.")
g("armor", "guardsmans_coif", "Guardsman's Coif", "Mail", "Head", 10, 5, G, st(Stamina=3, Strength=2),
  "Sergeant Holt's spare. He says it's lucky; it has a dent shaped like a goose.")
g("armor", "hornkin_gauntlets", "Hornkin-Riveted Gauntlets", "Mail", "Hands", 11, 6, G,
  st(Strength=3, Agility=1, Stamina=1), "Hammered on a mountain anvil and riveted with horn.")
g("armor", "stoneford_greaves", "Stoneford Greaves", "Mail", "Legs", 13, 8, G, st(Strength=3, Stamina=3, Intellect=2),
  "Built for wading cold rivers and standing your ground in them.")
g("armor", "wayfarers_chain_boots", "Wayfarer's Chain Boots", "Mail", "Feet", 9, 4, G, st(Stamina=2, Agility=2),
  "They have walked the pilgrim road more times than you have.")
# plate
g("armor", "vanguard_breastplate", "Vanguard Breastplate of the Lantern", "Plate", "Chest", 15, 10, G,
  st(Strength=4, Stamina=5), "A lantern is etched over the heart, worn smooth by a hundred gauntleted salutes.")
g("armor", "lantern_knight_gauntlets", "Lantern-Knight Gauntlets", "Plate", "Hands", 15, 10, G,
  st(Strength=3, Stamina=3), "Built to hold a lantern steady in a gale. Or a sword.")
# weapons
g("weapon", "thornwood_staff", "Thornwood Staff", "Staff", 10, 5, 3.0, G, st(Intellect=4, Spirit=3, Stamina=2),
  "Grown, not carved. A thorn still sprouts at the top every spring.")
g("weapon", "shepherds_crook", "Shepherd's Crook", "Staff", 7, 2, 2.9, G, st(Strength=2, Stamina=2, Spirit=2),
  "Excellent for retrieving lambs, villains and dropped sandwiches.")
g("weapon", "bandits_gutting_knife", "Bandit's Gutting Knife", "Dagger", 10, 5, 1.7, G, st(Agility=2, Stamina=1),
  "The handle is carved with tally marks. You decide not to count them.")
g("weapon", "tidecaller_knuckles", "Tidecaller Knuckles", "FistWeapon", 11, 6, 1.6, G, st(Agility=2, Strength=1),
  "Inlaid with island shell. They sound like the sea when you punch things.")
g("weapon", "woodsplitter", "Woodsplitter", "TwoHandAxe", 9, 4, 3.3, G, st(Strength=5, Stamina=2),
  "It has split a great deal of wood and is ready to branch out.")
g("weapon", "lamp_iron_mace", "Lamp-Iron Mace", "OneHandMace", 11, 6, 2.5, G, st(Strength=2, Intellect=2),
  "Forged from melted-down lamp brackets. It glows a little when swung in the dark.")
g("weapon", "brightsteel_shortsword", "Brightsteel Shortsword", "OneHandSword", 12, 7, 2.3, G,
  st(Strength=2, Stamina=2), "Garrow's finest work. He'll tell you so, too.")
g("weapon", "mosswood_longbow", "Mosswood Longbow", "Bow", 11, 6, 2.6, G, st(Agility=3, Stamina=1),
  "The string is spider silk. The spider was not consulted.")
g("weapon", "tinkers_hand_cannon", "Tinker's Hand-Cannon", "Gun", 9, 4, 2.6, G, st(Agility=2, Stamina=1),
  "Goes off roughly when you want it to, which is better than most.")
g("weapon", "rattling_crossbow", "Rattling Crossbow", "Crossbow", 10, 5, 3.0, G, st(Agility=2, Stamina=2),
  "Something inside it rattles. It has always rattled. Do not fix the rattle.")
g("weapon", "bridgekeepers_glaive", "Bridgekeeper's Glaive", "Polearm", 12, 7, 3.3, G,
  st(Strength=4, Agility=3, Stamina=2), "Long enough to keep a bandit at arm's length. Several arms, in fact.")
g("weapon", "lantern_glass_wand", "Lantern-Glass Wand", "Wand", 11, 6, 1.6, G, st(Spirit=2), school="Holy",
  desc="A sliver of spirit-lantern glass on a rowan rod. It remembers being bright.")
g("weapon", "hexers_fetish_wand", "Hexer's Fetish Wand", "Wand", 10, 5, 1.5, G, st(Intellect=2), school="Shadow",
  desc="Bundled bones, twine and one very sarcastic bead.")
g("weapon", "sprocket_dagger", "Sprocket Dagger", "Dagger", 6, 1, 1.6, G, st(Agility=1, Stamina=1),
  "A blade bolted to a cog. Pip insists the cog is load-bearing.")
g("weapon", "pips_throwing_sprockets", "Pip's Throwing Sprockets", "Thrown", 6, 1, 2.0, G, st(Agility=1),
  "Pip will want these back. Pip will not get these back.")
g("shield", "oaken_heater", "Oaken Heater Shield", 11, 6, G, st(Stamina=2, Strength=1),
  "Painted with a lantern. The paint is newer than the dents.")
g("held", "wayside_paper_lantern", "Wayside Paper Lantern", 9, 4, G, st(Intellect=2, Spirit=2),
  "A paper lantern from the Wayside Shrine. It glows whenever someone nearby is being kind.", icon="fire")
# accessories
g("acc", "nells_lucky_acorn", "Nell's Lucky Acorn", "Trinket", 8, 3, G, st(Spirit=2, Stamina=2),
  "Nell drew a face on it. It is, she insists, a very lucky face.", icon="leaf")
g("acc", "moppets_moss_charm", "Moppet's Moss Charm", "Neck", 8, 3, G, st(Spirit=3, Stamina=1),
  "A tuft of living moss on a string. It hums when spirits are near.", icon="leaf")
g("acc", "valley_brass_band", "Valley Brass Band", "Finger", 9, 4, G, st(Stamina=2, Strength=1),
  "Every Lanternvale child is given one at their naming. This one was never claimed.")
g("acc", "willow_ring", "Willow Ring", "Finger", 10, 5, G, st(Intellect=2, Spirit=1),
  "A loop of living willow, still in leaf.")
g("acc", "cutpurses_signet", "Cutpurse's Signet", "Finger", 10, 5, G, st(Agility=2, Stamina=1),
  "The crest is filed off. The crest was probably stolen too.")
g("acc", "wolf_tooth_necklace", "Wolf-Tooth Necklace", "Neck", 7, 2, G, st(Agility=2, Strength=1),
  "Bram made it from teeth he found in the pasture. He says the wolves won't miss them.", icon="fang")
g("acc", "mossy_cloak", "Mossy Cloak", "Back", 9, 4, G, st(Stamina=2, Spirit=1),
  "It is not so much a cloak as a very committed patch of moss.", icon="wings")
g("acc", "couriers_swift_cloak", "Courier's Swift Cloak", "Back", 8, 3, G, [{"stat": "Agility", "value": 2},
                                                                           pst("MoveSpeed", 3)],
  "Fennick's old cloak, patched with stamps. Letters never arrive late in it.", icon="wings")
g("acc", "braided_horn_charm", "Braided Horn-Charm", "Neck", 6, 1, G, st(Spirit=2, Stamina=1),
  "Hornkin braid one cord for every vow they keep.", icon="totem")

# ========================================================== companion starting gear
COMPANION_GEAR = {
    "kael": [
        g("weapon", "kael_notched_greatsword", "Kael's Notched Greatsword", "TwoHandSword", 7, 1, 3.4, G,
          st(Strength=3, Stamina=2), "Every notch is a story. Kael tells none of them.", unique=True),
        g("armor", "red_wanderers_greatcoat", "Red Wanderer's Greatcoat", "Mail", "Chest", 7, 1, G,
          st(Stamina=3, Strength=1), "Faded from crimson to the colour of old embers. Chainmail hides in the lining.",
          unique=True),
    ],
    "lys": [
        g("weapon", "obsidian_orb_staff", "Obsidian Orb Staff", "Staff", 7, 1, 3.0, G, st(Intellect=3, Stamina=1),
          "The orb holds a single, very patient storm.", unique=True),
        g("armor", "lys_belted_gown", "Gown of Many Belts", "Cloth", "Chest", 7, 1, G, st(Intellect=3, Spirit=1),
          "Fourteen belts. Each has a purpose. Lys will not tell you what they are.", unique=True),
    ],
    "seren": [
        g("weapon", "pilgrims_ringed_staff", "Pilgrim's Ringed Staff", "Staff", 7, 1, 3.0, G,
          st(Spirit=3, Intellect=2), "Its rings chime softly with every step of a pilgrimage.", unique=True),
        g("armor", "summoners_hakama_robe", "Summoner's Hakama Robe", "Cloth", "Chest", 7, 1, G,
          st(Spirit=2, Intellect=2), "Pale blue, tied with a sash her mother wore.", unique=True),
    ],
    "rook": [
        g("weapon", "saltreach_recurve", "Saltreach Recurve", "Bow", 7, 1, 2.5, G, st(Agility=2, Stamina=1),
          "Made from driftwood and stubbornness on the Saltreach Isles.", unique=True),
        g("weapon", "islanders_hand_axe", "Islander's Hand-Axe", "OneHandAxe", 6, 1, 2.5, G, st(Agility=1, Stamina=1),
          "Good for coconuts, kindling and arguments.", unique=True),
        g("armor", "ringball_captains_vest", "Ringball Captain's Vest", "Leather", "Chest", 7, 1, G,
          st(Agility=2, Stamina=2), "Number seven. Rook says it's lucky. Rook says most things are lucky.",
          unique=True),
    ],
    "pip": ["sprocket_dagger", "sprocket_dagger", "cogwright_goggles", "pips_throwing_sprockets"],
    "torvan": [
        g("weapon", "hornkin_spear_staff", "Hornkin Spear-Staff", "Staff", 7, 1, 3.2, G, st(Strength=2, Spirit=2),
          "A ceremonial staff tipped with a leaf-shaped blade, more vow than weapon.", unique=True),
        "braided_horn_charm",
        g("armor", "hornkin_fur_wrap", "Hornkin Fur Wrap", "Leather", "Chest", 7, 1, G, st(Stamina=2, Strength=1),
          "Mountain wool and stitched hide, warm enough for snowfields.", unique=True),
    ],
    "aldric": [
        g("weapon", "sunmere_hammer", "Sunmere Hammer", "OneHandMace", 7, 1, 2.6, G, st(Strength=2, Stamina=1),
          "Polished daily. Mostly so Aldric can see himself in it.", unique=True),
        g("shield", "sun_painted_kite_shield", "Sun-Painted Kite Shield", 7, 1, G, st(Stamina=1, Intellect=1),
          "The sun was painted by Aldric himself. It has a face. It is winking.", unique=True),
        g("armor", "sunmere_tabard_hauberk", "Sunmere Tabard Hauberk", "Mail", "Chest", 7, 1, G,
          st(Strength=2, Stamina=1, Intellect=1), "Bright gold over bright mail. Subtlety is for other knights.",
          unique=True),
    ],
    "morwen": [
        g("weapon", "morwens_letter_opener", "Morwen's Letter-Opener", "Dagger", 7, 1, 1.7, G, st(Intellect=2),
          "Silver, slender, and used exclusively for letters. Allegedly.", unique=True),
        g("held", "grimoire_of_polite_introductions", "Grimoire of Polite Introductions", 7, 1, G,
          st(Intellect=2, Spirit=1), "Chapter one: how to address a demon at dinner.", icon="demon", unique=True),
        g("armor", "ashcombe_ravenweave_coat", "Ashcombe Ravenweave Coat", "Cloth", "Chest", 7, 1, G,
          st(Intellect=2, Stamina=2), "High-collared, violet-lined and impossibly clean.", unique=True),
    ],
}

# =========================================================================== rares
R = "Rare"
RARES = {}


def r(kind, *a, **k):
    iid = {"armor": armor, "weapon": weapon, "shield": shield, "held": held, "acc": accessory}[kind](*a, **k)
    RARES[iid] = True
    return iid


r("acc", "greymanes_mantle", "Greymane's Mantle", "Back", 12, 7, R,
  [{"stat": "Agility", "value": 3}, {"stat": "Stamina", "value": 4},
   {"stat": "Resistance", "value": 5, "school": "Shadow"}],
  "The old alpha's silver ruff. The grey has washed out of it now; it is only silver.", icon="wolf")
r("weapon", "rusks_toll_taker", "Rusk's Toll-Taker", "OneHandAxe", 13, 8, 2.6, R, st(Strength=3, Stamina=3),
  "Its edge is notched with every 'toll' Rusk ever collected. There are fewer notches than he claimed.",
  effects=[{"type": "Proc", "proc": {"trigger": "OnMeleeHit", "ppm": 2,
                                     "effects": [{"type": "Damage", "min": 12, "max": 18, "school": "Physical"}]}}])
r("shield", "heartwood_bulwark", "Heartwood Bulwark", 13, 8, R, st(Stamina=4, Strength=2),
  "Cut from Rotheart's core, where the blight never reached. The rings still grow, very slowly.")
r("acc", "ishiros_prayer_beads", "Keeper Ishiro's Prayer Beads", "Neck", 14, 9, R,
  st(Intellect=3, Spirit=4, HealingPower=8),
  "One bead for every lantern on the pilgrim road. Ishiro counted them every night for forty years.",
  icon="hands_pray")
r("held", "komorebis_hanging_lantern", "Komorebi's Hanging Lantern", 12, 7, R,
  st(Intellect=3, Spirit=3, SpellDamage=4),
  "Dappled light spills from it the way sun falls through leaves.", icon="fire")
r("acc", "farmers_lucky_coin", "Farmer's Lucky Coin", "Trinket", 12, 7, R, st(Stamina=4, Spirit=3),
  "Rusk's last coin, which he refused to spend on anything but seed. Now it's yours. Plant something.",
  icon="coin")
# main quest rewards
r("weapon", "pilgrim_guardians_blade", "Pilgrim-Guardian's Blade", "OneHandSword", 15, 10, 2.5, R,
  st(Strength=4, Stamina=4), "Carried up the mountain beside every pilgrim for a hundred years. It came back every time.")
r("weapon", "staff_of_the_kindled_hearth", "Staff of the Kindled Hearth", "Staff", 15, 10, 3.0, R,
  st(Intellect=5, Spirit=5, Stamina=3, HealingPower=9),
  "A sliver of every hearth in Lanternvale burns in its crown.")
r("armor", "ember_stitched_jerkin", "Ember-Stitched Jerkin", "Leather", "Chest", 15, 10, R,
  st(Agility=6, Stamina=4), "The stitches glow faintly when you hold your breath.")
r("armor", "hauberk_of_the_long_night", "Hauberk of the Long Night", "Mail", "Chest", 15, 10, R,
  st(Strength=5, Stamina=5, Intellect=2), "Forged for those who stand watch until the lanterns come back.")
r("armor", "kindlewarm_robe", "Kindlewarm Robe", "Cloth", "Chest", 15, 10, R,
  st(Intellect=6, Spirit=4, Stamina=3), "Warm as a cat on a windowsill, in every weather.")
r("weapon", "bow_of_the_lantern_road", "Bow of the Lantern Road", "Bow", 15, 10, 2.7, R,
  st(Agility=5, Stamina=2), "Strung beneath Old Kusu. Its arrows always find their way home.")
# boss
r("acc", "antler_lantern_charm", "Antler-Lantern Charm", "Trinket", 16, 11, R,
  [{"stat": "Stamina", "value": 5}, {"stat": "Resistance", "value": 10, "school": "Shadow"}],
  "A fragment of the Warden's antler with a tiny flame curled inside it, asleep.", icon="fire")

# ============================================================================ epic
aura(id="cn_lanternbough_rekindle", name="Rekindled", icon="fire", kind="Buff", duration=10,
     description="Spell damage and healing increased by 15.", mods=st(SpellDamage=15, HealingPower=15))
EPIC = item(id="lanternbough", name="Lanternbough", icon="staff",
            description="Carved from an antler the Warden shed in a gentler year. A small lantern swings from the "
                        "crook, and wherever it goes, it is never quite dark.",
            kind="Weapon", quality="Epic", itemLevel=22, requiredLevel=12, equip="TwoHand", weaponType="Staff",
            minDamage=int(round((0.66 * 22 + 2) * 1.3 * 3.2 * 0.72)),
            maxDamage=int(round((0.66 * 22 + 2) * 1.3 * 3.2 * 1.28)), speed=3.2,
            stats=st(Intellect=10, Spirit=8, Stamina=7, SpellDamage=12, HealingPower=12),
            equipEffects=[{"type": "Proc", "proc": {"trigger": "OnSpellCast", "chance": 6, "internalCooldown": 30,
                                                    "effects": [{"type": "ApplyAura", "aura": "cn_lanternbough_rekindle",
                                                                 "target": "Self"}]}}],
            price=price(22, "TwoHand", "Epic", 1.2), unique=True)

# ===================================================================== suffixes
SUFFIXES = [
    ("suffix_bear", "of the Bear", ["Strength", "Stamina"], [1, 1]),
    ("suffix_boar", "of the Boar", ["Strength", "Spirit"], [1, 1]),
    ("suffix_eagle", "of the Eagle", ["Stamina", "Intellect"], [1, 1]),
    ("suffix_falcon", "of the Falcon", ["Agility", "Intellect"], [1, 1]),
    ("suffix_gorilla", "of the Gorilla", ["Strength", "Intellect"], [1, 1]),
    ("suffix_monkey", "of the Monkey", ["Agility", "Stamina"], [1, 1]),
    ("suffix_owl", "of the Owl", ["Intellect", "Spirit"], [1, 1]),
    ("suffix_tiger", "of the Tiger", ["Strength", "Agility"], [1, 1]),
    ("suffix_whale", "of the Whale", ["Stamina", "Spirit"], [1, 1]),
    ("suffix_wolf", "of the Wolf", ["Agility", "Spirit"], [1, 1]),
    ("suffix_stamina", "of Stamina", ["Stamina"], [1]),
    ("suffix_strength", "of Strength", ["Strength"], [1]),
    ("suffix_agility", "of Agility", ["Agility"], [1]),
    ("suffix_intellect", "of Intellect", ["Intellect"], [1]),
    ("suffix_spirit", "of Spirit", ["Spirit"], [1]),
    ("suffix_healing", "of Healing", ["HealingPower"], [1]),
    ("suffix_sorcery", "of Sorcery", ["Intellect", "Stamina", "SpellDamage"], [1, 1, 1]),
    ("suffix_fiery_wrath", "of Fiery Wrath", ["SpellDamage"], [1]),
    ("suffix_frozen_wrath", "of Frozen Wrath", ["SpellDamage"], [1]),
    ("suffix_shadow_wrath", "of Shadow Wrath", ["SpellDamage"], [1]),
    ("suffix_power", "of Power", ["AttackPower"], [1]),
    ("suffix_marksmanship", "of Marksmanship", ["RangedAttackPower"], [1]),
    ("suffix_defense", "of Defense", ["Defense"], [1]),
    ("suffix_concentration", "of Concentration", ["ManaRegen"], [1]),
    ("suffix_regeneration", "of Regeneration", ["HealthRegen"], [1]),
]

# ========================================================================= vendors
VENDOR_FOOD = ["food_rice_ball", "food_honey_cake", "food_mushroom_skewer", "food_lantern_stew",
               "drink_spring_water", "drink_barley_tea", "drink_melon_juice"]
VENDOR_GENERAL = (["potion_minor_healing", "potion_lesser_healing", "potion_minor_mana", "potion_minor_rejuvenation",
                   "potion_rage", "elixir_lions_strength", "elixir_minor_agility", "elixir_minor_fortitude",
                   "elixir_minor_defense", "elixir_wisdom", "elixir_lantern_oil", "bandage_linen", "bandage_wool",
                   "food_rice_ball", "food_honey_cake", "drink_spring_water", "drink_barley_tea",
                   "reagent_light_feather", "reagent_ankh", "reagent_flash_powder", "reagent_holy_candle",
                   "reagent_arcane_powder"] + VENDOR_WANDS)


def build():
    suffixes = [{"id": i, "name": n, "stats": s, "weights": w} for i, n, s, w in SUFFIXES]
    return {
        "_note": "Lanternvale content items: consumables (with hidden use_* abilities and cn_* auras), quest items, "
                 "junk, reagents, vendor commons, named greens/blues, the Warden's epic, companion starting gear and "
                 "random-green suffixes. Prices are buy prices in copper (items sell for 1/4).",
        "items": ITEMS, "itemSuffixes": suffixes, "abilities": ABIL, "auras": AURAS,
    }
