"""map_lanternvale.json, map_whisperwood.json, map_shrine.json

Coordinates in metres. x in [0,width] left->right, y in [0,depth]: y=0 is the FRONT edge (nearest the camera, bottom
of the screen), y=depth is the BACK (horizon). Larger y = further away, drawn behind.
"""
from wn_common import *

# ------------------------------------------------------------------ prop footprints (collider ellipse w, h)
FOOT = {
    "prop_cottage_a": (5.0, 2.2), "prop_cottage_b": (4.4, 2.0), "prop_cottage_c": (5.0, 2.2), "prop_inn": (7.0, 2.6),
    "prop_shop_stall": (3.0, 1.1), "prop_smithy": (5.0, 2.2), "prop_windmill": (2.6, 1.0), "prop_well": (1.8, 0.9),
    "prop_fence": (3.0, 0.35), "prop_lamp_post": (0.4, 0.3), "prop_spirit_lantern": (0.9, 0.5),
    "prop_spirit_lantern_dark": (0.9, 0.5), "prop_tree_oak": (1.2, 0.7), "prop_tree_pine": (1.0, 0.6),
    "prop_tree_birch": (0.7, 0.4), "prop_tree_great": (3.4, 1.6), "prop_tree_dead": (1.0, 0.6),
    "prop_bush_a": (1.2, 0.6), "prop_bush_b": (1.2, 0.6), "prop_rock_large": (2.0, 1.0), "prop_stump": (0.9, 0.5),
    "prop_log": (2.2, 0.6), "prop_cart": (2.0, 1.0), "prop_barrel": (0.8, 0.5), "prop_crate": (0.9, 0.6),
    "prop_hay": (1.2, 0.7), "prop_signpost": (0.4, 0.3), "prop_noticeboard": (1.6, 0.5), "prop_bench": (1.4, 0.4),
    "prop_campfire": (1.0, 0.6), "prop_tent": (2.4, 1.2), "prop_ruin_pillar": (1.1, 0.6),
    "prop_spirit_statue": (0.8, 0.5), "prop_blight_crystal": (1.2, 0.7), "prop_banner": (0.4, 0.3),
}
NO_COLLIDER = {"prop_rock_small", "prop_mushrooms", "prop_ruin_arch", "prop_shrine_gate", "prop_bridge",
               "decal_path_dirt", "decal_path_stone", "decal_flowers", "decal_blight"}
SWAY = {"prop_tree_oak", "prop_tree_pine", "prop_tree_birch", "prop_tree_great", "prop_bush_a", "prop_bush_b",
        "prop_banner", "fg_grass_a", "fg_grass_b", "fg_flowers_a", "fg_flowers_b", "fg_ferns"}


def light(color="#ffd9a0", radius=4.0, intensity=1.0, offset=(0, 0), flicker=False, night=False):
    d = {"color": color, "radius": radius, "intensity": intensity}
    if offset != (0, 0): d["offset"] = list(offset)
    if flicker: d["flicker"] = True
    if night: d["nightOnly"] = True
    return d


LAMP = lambda: light("#ffcf8a", 4.0, 0.9, (0, 2.3), flicker=True, night=True)
SPIRIT = lambda: light("#ffd27a", 3.5, 0.9, (0, 1.6), flicker=True)
WINDOW = lambda off=(1.2, 2.4): light("#ffc070", 3.5, 0.7, off, night=True)
FIRE = lambda r=4.5: light("#ffa050", r, 1.1, (0, 0.5), flicker=True)
CRYSTAL = lambda: light("#b49cff", 2.6, 0.6, (0, 1.0))
MUSH = lambda: light("#8ff0d2", 2.0, 0.6, (0, 0.3))


def P(art, x, y, scale=1.0, flip=False, light_=None, text=None, interact=None, tint=None, collider=True):
    d = {"art": art, "pos": [x, y]}
    if scale != 1.0: d["scale"] = scale
    if flip: d["flip"] = True
    if collider and art in FOOT:
        w, h = FOOT[art]
        d["collider"] = {"w": round(w * scale, 2), "h": round(h * scale, 2)}
    if light_: d["light"] = light_
    if tint: d["tint"] = tint
    if art in SWAY: d["sway"] = True
    if interact: d["interact"] = interact
    if text: d["text"] = text
    return d


def FG(art, x, y, scale=1.0, flip=False):
    d = {"art": art, "pos": [x, y]}
    if scale != 1.0: d["scale"] = scale
    if flip: d["flip"] = True
    if art in SWAY: d["sway"] = True
    return d


def L(art, parallax, y, height, tint=None, scroll=0.0):
    d = {"art": art, "parallax": parallax, "y": y, "height": height, "loop": True}
    if tint: d["tint"] = tint
    if scroll: d["scrollSpeed"] = scroll
    return d


def NPC(npc, x, y, flip=False, require=None, hide=None):
    d = {"npc": npc, "pos": [x, y]}
    if flip: d["flip"] = True
    if require: d["requireFlag"] = require
    if hide: d["hideFlag"] = hide
    return d


def ENC(id, x, y, radius, enemies, require=None, done=None, dialogue=None, hidden=False):
    d = {"id": id, "pos": [x, y], "radius": radius,
         "enemies": [{"creature": c, "pos": [ex, ey]} for c, ex, ey in enemies]}
    if require: d["requireFlag"] = require
    d["doneFlag"] = done or ("enc_" + id[4:] if id.startswith("enc_") else "enc_" + id)
    if dialogue: d["dialogue"] = dialogue
    if hidden: d["hidden"] = True
    return d


def CHEST(id, x, y, loot=None, items=None, gold=0, dc=0, require=None):
    d = {"id": id, "pos": [x, y], "art": "prop_chest"}
    if loot: d["lootTable"] = loot
    if items: d["items"] = items
    if gold: d["gold"] = gold
    if dc: d["lockCheck"] = {"skill": "SleightOfHand", "dc": dc}
    if require: d["requireFlag"] = require
    return d


def TR(id, x, y, w, h, target, spawn, label, require=None, locked=None):
    d = {"id": id, "pos": [x, y], "size": [w, h], "targetMap": target, "targetSpawn": spawn, "label": label}
    if require: d["requireFlag"] = require
    if locked: d["lockedText"] = locked
    return d


def REG(id, x, y, w, h, text, flag=False):
    d = {"id": id, "pos": [x, y], "size": [w, h]}
    if flag: d["enterFlag"] = id
    d["text"] = text
    return d


def SP(id, x, y): return {"id": id, "pos": [x, y]}


def path(art, xs_ys):
    return [P(art, x, y, collider=False) for x, y in xs_ys]


# =========================================================================== LANTERNVALE
lv_props = []
lv_props += path("decal_path_dirt", [(x, 5.0 + (0.3 if (x // 8) % 2 else -0.2)) for x in range(4, 90, 8)])
lv_props += path("decal_path_stone", [(25, 7.2), (25, 8.6)])
lv_props += path("decal_path_dirt", [(43, 7.4), (56.5, 7.6), (67, 7.4)])
lv_props += [P("decal_flowers", x, y, collider=False) for x, y in
             [(9, 9.6), (18, 10.8), (28.5, 7.4), (36, 10.2), (51, 9.8), (63.5, 10.6), (74, 9.2), (3, 2.0),
              (31, 2.0), (62, 2.2)]]
lv_props += [
    # west entrance
    P("prop_tree_oak", 2.0, 9.4), P("prop_tree_birch", 0.8, 12.6),
    P("prop_cottage_a", 6.5, 11.6, light_=WINDOW((1.3, 2.4))),
    P("prop_signpost", 7.2, 3.0, text="East: Lanternvale Square, the Sleepy Lantern and Whisperwood. West: the long "
                                      "road home, which can wait."),
    P("prop_lamp_post", 9.6, 7.0, light_=LAMP()), P("prop_bush_a", 11.6, 9.8), P("prop_fence", 10.5, 2.3),
    P("prop_cottage_b", 13.8, 12.2, light_=WINDOW((-1.0, 3.0))), P("prop_barrel", 10.6, 10.6),
    P("prop_lamp_post", 15.2, 3.4, light_=LAMP()),
    # the square & Old Kusu
    P("prop_tree_great", 25.0, 12.3, text="Old Kusu, the great camphor tree. A shimenawa rope circles its trunk, "
                                          "hung with paper charms that rustle without any wind."),
    P("prop_spirit_lantern_dark", 22.6, 9.8, 1.15, interact="great_lantern",
      text="The great lantern beneath Old Kusu. Its stone is cold, and smells faintly of ash."),
    P("prop_spirit_lantern_dark", 27.4, 9.8, 1.15, interact="great_lantern_twin",
      text="A twin of the great lantern, dark as a closed eye."),
    P("prop_spirit_lantern", 19.6, 9.2, light_=SPIRIT()), P("prop_spirit_lantern", 31.4, 9.0, light_=SPIRIT()),
    P("prop_noticeboard", 17.4, 8.8, text="NOTICES — Wolves in the east pasture: see Bram. Wicks stolen (AGAIN): see "
                                          "Tobben. Bandits at the Old Bridge: see Sgt. Holt. LOST: one Moppet, "
                                          "small, jingly. See Nell. Reward: a very lucky acorn."),
    P("prop_bench", 19.6, 7.6), P("prop_bench", 30.0, 11.0),
    P("prop_well", 33.6, 7.8), P("prop_bush_b", 35.6, 10.8), P("prop_lamp_post", 34.8, 3.4, light_=LAMP()),
    P("prop_fence", 21.0, 2.3), P("prop_rock_small", 28.0, 2.6),
    # market & inn
    P("prop_inn", 43.0, 12.0, light_=WINDOW((-1.6, 3.2)), text="The Sleepy Lantern. A painted sign shows a lantern "
                                                               "wearing a nightcap."),
    P("prop_barrel", 38.8, 9.8), P("prop_barrel", 39.6, 9.4), P("prop_crate", 49.0, 10.0),
    P("prop_bench", 47.4, 9.6), P("prop_lamp_post", 38.2, 7.4, light_=LAMP()),
    P("prop_lamp_post", 49.6, 7.4, light_=LAMP()),
    P("prop_shop_stall", 40.0, 2.3), P("prop_shop_stall", 45.6, 2.3, flip=True), P("prop_crate", 43.0, 1.4),
    P("prop_cart", 52.0, 2.4), P("prop_crate", 50.2, 1.4), P("prop_barrel", 53.8, 1.3),
    # smithy
    P("prop_smithy", 56.5, 11.4, light_=light("#ff9a4a", 3.8, 1.0, (0.8, 1.0), flicker=True)),
    P("prop_crate", 60.2, 9.8), P("prop_barrel", 52.6, 10.2), P("prop_banner", 61.6, 10.6),
    P("prop_lamp_post", 59.8, 3.4, light_=LAMP()),
    # trainers' hall & training yard
    P("prop_cottage_c", 67.0, 12.2, light_=WINDOW((1.5, 2.6)),
      text="The Trainers' Hall. Someone has carved 'ELBOWS UP' over the door."),
    P("prop_banner", 63.4, 10.8), P("prop_banner", 71.0, 10.8), P("prop_tree_birch", 74.4, 12.0),
    P("prop_fence", 65.0, 2.6), P("prop_fence", 71.0, 2.6), P("prop_hay", 72.8, 1.2),
    P("prop_lamp_post", 74.6, 3.4, light_=LAMP()),
    # windmill on the back hill
    P("prop_windmill", 79.0, 13.7, text="Hollis's windmill. Its sails hang perfectly still."),
    P("prop_tree_pine", 75.8, 13.6), P("prop_tree_oak", 88.4, 13.2),
    # east pasture
    P("prop_fence", 79.5, 8.6), P("prop_fence", 82.5, 8.6), P("prop_hay", 80.2, 11.0), P("prop_hay", 83.4, 12.4),
    P("prop_bush_a", 88.6, 9.6),
    P("prop_signpost", 82.4, 6.9, flip=True,
      text="East: Whisperwood and the Pilgrim Road. Mind the boars. Mind the wolves. Mind yourself."),
    P("prop_lamp_post", 86.0, 7.2, light_=LAMP()), P("prop_rock_small", 77.0, 2.0),
]
lv_fg = [FG("fg_grass_a", 3, -0.4), FG("fg_flowers_a", 13, -0.2), FG("fg_stones_a", 24, -0.6),
         FG("fg_grass_b", 33, -0.3), FG("fg_flowers_b", 44, -0.5), FG("fg_ferns", 57, -0.4),
         FG("fg_grass_a", 66, -0.2, flip=True), FG("fg_stones_b", 77, -0.6), FG("fg_flowers_a", 86, -0.3, flip=True)]
LANTERNVALE = {
    "id": "lanternvale", "name": "Lanternvale", "subtitle": "A village beneath the great camphor tree",
    "width": 90, "depth": 14, "skyTop": "#9fd3f0", "skyBottom": "#fdf1d6",
    "layers": [L("bg_clouds", 0.05, 15.5, 6, scroll=0.15), L("bg_mountains_far", 0.1, 12.0, 9),
               L("bg_hills_far", 0.25, 12.5, 7), L("bg_village_far", 0.4, 13.0, 6)],
    "ground": "ground_village", "groundTile": 8,
    "props": lv_props, "foreground": lv_fg,
    "npcs": [
        NPC("villager_june", 11.0, 7.2), NPC("guard_holt", 16.2, 7.6),
        NPC("elder_maru", 25.0, 7.6), NPC("child_nell", 21.6, 6.6),
        NPC("moppet", 22.4, 7.0, require="moppet_found"),
        NPC("lamplighter_tobben", 28.8, 9.0, flip=True), NPC("aldric", 30.0, 6.6, flip=True, hide="recruited_aldric"),
        NPC("child_toby", 35.2, 6.4, flip=True),
        NPC("merchant_tilly", 40.0, 3.6), NPC("innkeeper_dorrit", 43.0, 8.8),
        NPC("kael", 47.4, 9.1, flip=True, hide="recruited_kael"),
        NPC("pip", 52.0, 3.7, hide="recruited_pip"),
        NPC("smith_garrow", 55.2, 8.6), NPC("armorer_bess", 58.8, 8.6, flip=True),
        NPC("trainer_odo", 63.6, 8.2), NPC("trainer_quillon", 66.4, 9.2), NPC("trainer_wick", 69.4, 8.2, flip=True),
        NPC("trainer_fennel", 72.4, 9.2, flip=True),
        NPC("villager_hollis", 77.2, 10.6), NPC("shepherd_bram", 76.6, 7.0),
        NPC("rusk", 79.6, 7.2, flip=True, require="bandits_peaceful"),
        NPC("postman_fennick", 80.4, 4.0, flip=True),
    ],
    "encounters": [
        ENC("enc_training_dummy", 68.0, 1.5, 0.9, [("cr_training_dummy", 68.0, 1.2)], done="training_dummy_done"),
        ENC("enc_pasture_wolves", 85.2, 11.4, 3.2,
            [("cr_wolf", 84.8, 11.6), ("cr_wolf", 86.4, 12.6), ("cr_wolf", 86.8, 10.8)],
            require="shepherd_quest", done="pasture_wolves_done"),
    ],
    "chests": [CHEST("chest_village_oak", 1.4, 11.6, loot="lt_chest_village")],
    "transitions": [TR("to_whisperwood", 89.3, 5.0, 1.4, 6.0, "whisperwood", "from_village", "Whisperwood")],
    "spawns": [SP("default", 4.0, 5.0), SP("camphor", 25.0, 6.0), SP("from_whisperwood", 86.0, 4.6)],
    "regions": [
        REG("reg_old_kusu", 25.0, 9.0, 14, 7, "Old Kusu — the great camphor tree. Four hundred years old, and still "
                                              "growing."),
        REG("reg_sleepy_lantern", 43.0, 8.0, 10, 6, "The Sleepy Lantern — beds, stew and gossip."),
        REG("reg_training_yard", 68.0, 6.0, 12, 8, "The Trainers' Hall and training yard."),
        REG("reg_east_pasture", 83.5, 10.5, 12, 7, "Bram's pasture. The grass is greying at the tips."),
    ],
    "ambient": {"fireflies": True, "pollen": True, "timeOfDay": "day", "dayNightCycle": True,
                "ambientColor": "#fff6e8", "ambientIntensity": 1.0},
    "restArea": False, "music": "music_lanternvale",
}

# =========================================================================== WHISPERWOOD
ww_props = []
ww_props += path("decal_path_dirt", [(4, 6.0), (12, 6.4), (20, 6.0), (28, 5.6), (36, 6.0), (44, 6.4), (52, 6.0),
                                     (64, 6.2), (72, 6.6), (80, 6.2), (88, 6.6), (95, 7.0)])
ww_props += path("decal_path_stone", [(40, 8.4)])
ww_props += [P("decal_flowers", x, y, collider=False) for x, y in [(8, 2.4), (33, 9.4), (47, 9.8), (57, 12.6)]]
ww_props += [P("decal_blight", x, y, collider=False) for x, y in
             [(79, 9.0), (84, 4.6), (88, 10.4), (92.5, 6.6), (96, 3.0), (75, 12.4)]]
ww_props += [
    # forest edge & Rook's camp
    P("prop_tree_oak", 2.0, 12.2), P("prop_tree_birch", 5.4, 10.8), P("prop_tree_pine", 8.8, 13.4),
    P("prop_bush_b", 1.2, 9.2), P("prop_tree_birch", 1.6, 1.6),
    P("prop_signpost", 6.2, 3.6, text="West: Lanternvale. East: the Wayside Shrine, the Old Bridge and the pilgrim "
                                      "stair. (Someone has added: BEWARE MOSSLINGS.)"),
    P("prop_tent", 12.0, 11.8), P("prop_campfire", 13.6, 9.6, light_=FIRE()), P("prop_log", 11.0, 9.0),
    P("prop_barrel", 10.2, 11.0),
    # boar clearing
    P("prop_tree_oak", 17.0, 13.2), P("prop_bush_a", 21.6, 13.4), P("prop_stump", 15.6, 2.0),
    # Greymane's den
    P("prop_rock_large", 22.4, 12.9), P("prop_rock_large", 26.6, 13.3, flip=True), P("prop_rock_small", 24.6, 13.7),
    P("prop_tree_dead", 28.4, 13.1),
    P("prop_tree_pine", 31.0, 1.2), P("prop_rock_small", 27.0, 1.0),
    # mushroom ring (Moppet)
    P("prop_mushrooms", 34.2, 13.3, light_=MUSH()), P("prop_mushrooms", 36.8, 13.6, light_=MUSH()),
    P("prop_mushrooms", 35.6, 12.6), P("prop_stump", 32.6, 13.5), P("prop_tree_birch", 31.4, 12.0),
    # Wayside Shrine
    P("prop_shrine_gate", 40.0, 10.0, text="A vermilion gate, its paint flaking. A wooden tablet reads: 'Halfway. "
                                           "Rest your feet, not your heart.'"),
    P("prop_spirit_lantern_dark", 42.2, 12.2, 1.1, interact="wayside_lantern",
      text="The Wayside lantern. A faint warmth lingers in the stone, like a hand just withdrawn."),
    P("prop_spirit_statue", 37.8, 12.0, text="A small stone fox wearing a red bib. Someone has left it a rice ball."),
    P("prop_tree_oak", 35.0, 1.4), P("prop_tree_oak", 47.6, 13.4), P("prop_bush_a", 45.0, 10.6),
    # Puddlecap Hollow (Mossling camp)
    P("prop_mushrooms", 50.0, 11.6, light_=MUSH()), P("prop_mushrooms", 54.4, 12.6), P("prop_mushrooms", 51.6, 13.4),
    P("prop_stump", 49.0, 12.8), P("prop_stump", 55.2, 11.0),
    P("prop_lamp_post", 52.6, 13.2, 0.4, light_=light("#ffe08a", 2.2, 0.8, (0, 0.9), flicker=True),
      text="A tiny lantern made from a thimble, hung on a twig. It burns a very nibbled wick."),
    P("prop_bush_b", 44.0, 1.6), P("prop_rock_small", 48.6, 1.2),
    # the Old Bridge
    P("prop_bridge", 59.0, 6.2, 1.3, text="The Old Bridge. Someone has painted 'TOLL' on it, and someone else has "
                                          "added a very small sad face."),
    P("prop_rock_large", 59.0, 10.4), P("prop_rock_large", 58.6, 2.0, flip=True), P("prop_bush_b", 60.8, 9.0),
    P("prop_tent", 64.2, 11.8), P("prop_campfire", 63.0, 10.0, light_=FIRE(4.0)), P("prop_banner", 61.6, 11.0),
    P("prop_crate", 66.0, 10.4), P("prop_barrel", 66.8, 9.8),
    # Spiders' Hollow
    P("prop_tree_pine", 68.0, 13.7), P("prop_log", 70.0, 12.6), P("prop_rock_large", 74.2, 13.0),
    P("prop_tree_pine", 66.0, 1.0), P("prop_stump", 72.6, 1.6),
    # the Grey Grove
    P("prop_tree_dead", 77.0, 13.0), P("prop_tree_dead", 83.4, 13.6, flip=True), P("prop_tree_dead", 89.6, 12.8),
    P("prop_blight_crystal", 77.2, 2.8, light_=CRYSTAL()), P("prop_blight_crystal", 80.6, 12.2, light_=CRYSTAL()),
    P("prop_blight_crystal", 86.6, 11.8, light_=CRYSTAL()), P("prop_blight_crystal", 91.2, 2.4, light_=CRYSTAL()),
    P("prop_spirit_lantern_dark", 82.0, 9.0), P("prop_tree_dead", 85.0, 1.2),
    # foot of the stair, choked with grey thorns
    P("prop_ruin_arch", 96.6, 12.6, text="The first arch of the pilgrim stair, wound tight with grey, thorned roots."),
    P("prop_tree_dead", 98.4, 10.6), P("prop_blight_crystal", 98.2, 3.0, light_=CRYSTAL()),
    P("prop_spirit_lantern_dark", 94.0, 11.6),
]
ww_fg = [FG("fg_ferns", 2, -0.4), FG("fg_grass_b", 10, -0.2), FG("fg_stones_a", 18, -0.6),
         FG("fg_ferns", 27, -0.3, flip=True), FG("fg_flowers_b", 36, -0.5), FG("fg_grass_a", 45, -0.2),
         FG("fg_ferns", 54, -0.4), FG("fg_stones_b", 63, -0.6), FG("fg_ferns", 72, -0.3, flip=True),
         FG("fg_grass_b", 81, -0.4), FG("fg_stones_a", 90, -0.6, flip=True), FG("fg_ferns", 98, -0.3)]
WHISPERWOOD = {
    "id": "whisperwood", "name": "Whisperwood", "subtitle": "The old pilgrim road",
    "width": 100, "depth": 15, "skyTop": "#a8d8c8", "skyBottom": "#f4f0d0",
    "layers": [L("bg_clouds", 0.05, 17.0, 6, scroll=0.1), L("bg_mountains_far", 0.1, 13.0, 9),
               L("bg_forest_far", 0.3, 13.5, 8), L("bg_forest_near", 0.55, 14.2, 8)],
    "ground": "ground_forest", "groundTile": 8,
    "props": ww_props, "foreground": ww_fg,
    "npcs": [
        NPC("rook", 15.0, 9.4, flip=True, hide="recruited_rook"),
        NPC("moppet", 35.6, 13.0, hide="moppet_found"),
        NPC("seren", 41.0, 10.6, hide="recruited_seren"),
        NPC("komorebi", 43.4, 11.6, flip=True),
        NPC("lys", 78.6, 4.0, flip=True, hide="recruited_lys"),
    ],
    "encounters": [
        ENC("enc_boars_edge", 18.0, 3.0, 3.0, [("cr_boar", 17.4, 2.4), ("cr_boar", 19.0, 3.4)]),
        ENC("enc_greymane", 24.6, 11.4, 3.4,
            [("cr_greymane", 24.6, 12.3), ("cr_wolf", 23.0, 11.4), ("cr_wolf", 26.2, 11.2)],
            require="shepherd_hunt", done="greymane_dead"),
        ENC("enc_forest_wolves", 29.6, 2.8, 3.0,
            [("cr_wolf", 29.0, 2.2), ("cr_wolf", 30.6, 3.2), ("cr_wolf", 29.0, 1.4)]),
        ENC("enc_mossling_scamps", 46.8, 2.8, 2.8,
            [("cr_mossling", 46.0, 2.2), ("cr_mossling", 47.8, 3.2), ("cr_mossling_shaman", 47.0, 1.4)]),
        ENC("enc_mossling_camp", 52.0, 11.2, 3.4,
            [("cr_mossling_chief", 52.4, 12.2), ("cr_mossling", 50.6, 11.6), ("cr_mossling", 54.0, 11.4),
             ("cr_mossling_shaman", 53.2, 12.9)], done="mossling_camp_done", dialogue="dlg_puddlecap"),
        ENC("enc_bandit_lookouts", 56.0, 5.4, 2.5,
            [("cr_bandit_archer", 56.8, 7.2), ("cr_bandit_cutthroat", 57.0, 4.6)],
            done="lookouts_done", dialogue="dlg_bandit_lookouts"),
        ENC("enc_bridge_toll", 62.6, 7.2, 3.4,
            [("cr_bandit_chief", 63.2, 8.0), ("cr_bandit_cutthroat", 61.8, 6.2), ("cr_bandit_archer", 64.6, 9.2),
             ("cr_bandit_hexer", 65.0, 6.4)], done="bandits_dealt_with", dialogue="dlg_rusk"),
        ENC("enc_boars_road", 68.4, 3.0, 2.8, [("cr_boar", 67.8, 2.6), ("cr_boar", 69.4, 3.6)]),
        ENC("enc_spiders", 70.4, 10.8, 3.0,
            [("cr_spider", 69.2, 11.6), ("cr_spider", 71.4, 11.9), ("cr_spider", 70.6, 9.8)], hidden=True),
        ENC("enc_wisps_grove", 82.2, 10.6, 3.2, [("cr_hollow_wisp", 81.4, 11.2), ("cr_hollow_wisp", 83.4, 11.6)]),
        ENC("enc_wisps_hollow", 87.0, 3.4, 3.0,
            [("cr_hollow_wisp", 86.2, 2.8), ("cr_hollow_wisp", 88.2, 3.6), ("cr_wolf_blighted", 87.6, 2.0)]),
        ENC("enc_blighted_wolves", 88.0, 12.6, 2.4,
            [("cr_wolf_blighted", 87.2, 13.2), ("cr_wolf_blighted", 88.6, 11.8)]),
        ENC("enc_rotheart", 93.6, 8.4, 3.2, [("cr_hollow_treant", 95.4, 8.6)], require="embers_gathered",
            done="rotheart_defeated"),
    ],
    "chests": [
        CHEST("chest_mossling_stash", 44.6, 3.6, loot="lt_chest_forest"),
        CHEST("chest_moppet_stump", 33.2, 12.6, loot="lt_chest_moppet", require="moppet_secret"),
        CHEST("chest_bandit_strongbox", 67.2, 11.4, loot="lt_chest_forest_locked", dc=13),
        CHEST("chest_satchel", 72.2, 13.0, loot="lt_chest_spider_hollow", items=["fennicks_satchel"]),
        CHEST("chest_grey_offering", 92.6, 13.4, loot="lt_chest_forest_locked", dc=15),
    ],
    "transitions": [
        TR("to_lanternvale", 0.7, 6.0, 1.4, 6.0, "lanternvale", "from_whisperwood", "Lanternvale"),
        TR("to_shrine", 99.3, 7.0, 1.4, 6.0, "shrine", "from_whisperwood", "The Old Lantern Shrine",
           require="rotheart_defeated",
           locked="A wall of grey, thorned roots chokes the pilgrim stair. Something huge breathes behind it."),
    ],
    "spawns": [SP("default", 3.6, 6.0), SP("from_village", 3.6, 6.0), SP("from_shrine", 97.6, 3.6)],
    "regions": [
        REG("reg_whisperwood_edge", 7.0, 7.5, 12, 15, "Whisperwood — the old pilgrim road winds east beneath the "
                                                      "trees."),
        REG("reg_wayshrine", 41.0, 9.5, 12, 10, "The Wayside Shrine — halfway up the pilgrim road. Its lantern is "
                                                "dark.", flag=True),
        REG("reg_puddlecap_hollow", 52.0, 11.5, 8, 6, "Puddlecap Hollow. Tiny tents, tiny footprints, very large "
                                                      "hat."),
        REG("reg_old_bridge", 60.5, 7.0, 10, 14, "The Old Bridge."),
        REG("reg_spider_hollow", 71.0, 11.8, 8, 5, "The Spiders' Hollow — webs glitter between the pines."),
        REG("reg_grey_grove", 86.0, 7.5, 16, 15, "The Grey Grove. Colour drains from everything here. Even your "
                                                 "breath looks grey.", flag=True),
    ],
    "ambient": {"leaves": True, "mist": True, "timeOfDay": "day", "dayNightCycle": True,
                "ambientColor": "#e8f4dc", "ambientIntensity": 0.95},
    "restArea": True, "music": "music_whisperwood",
}

# =========================================================================== SHRINE
sh_props = []
sh_props += path("decal_path_stone", [(5, 6.0), (13, 6.2), (21, 6.4), (29, 6.8), (37, 7.0), (45, 7.2), (53, 7.6),
                                      (61, 8.0)])
sh_props += [P("decal_blight", x, y, collider=False) for x, y in
             [(26, 3.0), (30, 10.6), (38, 9.4), (42, 4.0), (50, 10.8), (57, 4.2), (60, 11.0), (65, 5.0)]]
sh_props += [
    # approach & pilgrims' camp
    P("prop_tent", 3.2, 12.2), P("prop_campfire", 5.4, 10.6, light_=FIRE(5.0)), P("prop_log", 6.6, 12.4),
    P("prop_shrine_gate", 9.0, 9.8, text="The vermilion gate of the Old Lantern Shrine. Above it, the stair "
                                         "climbs into dusk, lined with dark lanterns."),
    P("prop_spirit_lantern_dark", 7.0, 8.6), P("prop_spirit_lantern_dark", 11.2, 8.6),
    P("prop_spirit_statue", 14.6, 11.6, text="A small stone deer, its lacquer worn away by forty years of a "
                                             "keeper's hand."),
    P("prop_ruin_pillar", 17.0, 12.6), P("prop_ruin_pillar", 21.0, 13.0, flip=True),
    P("prop_blight_crystal", 19.4, 12.0, light_=CRYSTAL()), P("prop_rock_small", 2.0, 1.4),
    P("prop_tree_pine", 1.2, 9.4), P("prop_bush_b", 15.0, 1.6),
    # pilgrim terraces
    P("prop_spirit_lantern_dark", 23.4, 9.8), P("prop_spirit_statue", 24.2, 12.0),
    P("prop_ruin_arch", 32.0, 12.8), P("prop_blight_crystal", 38.0, 12.2, light_=CRYSTAL()),
    P("prop_blight_crystal", 30.4, 1.4, light_=CRYSTAL()), P("prop_spirit_lantern_dark", 34.6, 10.4),
    P("prop_tree_dead", 27.0, 13.4), P("prop_rock_large", 39.4, 1.2),
    # keeper's hall
    P("prop_spirit_lantern_dark", 41.0, 11.6), P("prop_spirit_lantern_dark", 43.6, 12.0),
    P("prop_spirit_lantern_dark", 46.2, 11.6), P("prop_ruin_pillar", 40.0, 13.2), P("prop_ruin_pillar", 48.6, 13.4),
    P("prop_bench", 50.0, 3.2, text="A keeper's bench, worn smooth. A cup of tea sits on it, long cold."),
    # sanctum
    P("prop_rock_large", 54.0, 12.8), P("prop_rock_large", 68.2, 12.6, flip=True),
    P("prop_ruin_pillar", 56.6, 13.4), P("prop_ruin_pillar", 66.6, 13.4, flip=True),
    P("prop_spirit_lantern_dark", 62.0, 12.4, 2.2, interact="heart_lantern",
      light_=light("#ff9a6a", 3.0, 0.5, (0, 3.2), flicker=True),
      text="The Heart Lantern. Deep inside, a single ember still glows, small as a held breath."),
    P("prop_blight_crystal", 58.0, 11.8, light_=CRYSTAL()), P("prop_blight_crystal", 66.0, 2.0, light_=CRYSTAL()),
    P("prop_spirit_statue", 69.0, 9.4),
]
sh_fg = [FG("fg_stones_a", 3, -0.5), FG("fg_ferns", 12, -0.3), FG("fg_stones_b", 21, -0.6),
         FG("fg_grass_b", 30, -0.3), FG("fg_stones_a", 39, -0.6, flip=True), FG("fg_ferns", 48, -0.4),
         FG("fg_stones_b", 57, -0.5, flip=True), FG("fg_grass_a", 66, -0.3)]
SHRINE = {
    "id": "shrine", "name": "The Old Lantern Shrine", "subtitle": "Where the Heart Lantern sleeps",
    "width": 70, "depth": 14, "skyTop": "#3b3a63", "skyBottom": "#e8a77c",
    "layers": [L("bg_clouds", 0.05, 16.0, 6, tint="#d6a6bc", scroll=0.08),
               L("bg_mountains_far", 0.1, 12.0, 9, tint="#8a7fb0"),
               L("bg_forest_far", 0.3, 12.4, 8, tint="#6f6c94"),
               L("bg_shrine_cliffs", 0.45, 12.8, 9, tint="#c8bcdc")],
    "ground": "ground_shrine", "groundTile": 8, "groundTint": "#e2dcec",
    "props": sh_props, "foreground": sh_fg,
    "npcs": [
        NPC("torvan", 12.6, 7.6, flip=True, hide="recruited_torvan"),
        NPC("morwen", 18.8, 11.0, hide="recruited_morwen"),
        NPC("warden_spirit", 62.0, 9.4, require="warden_defeated"),
    ],
    "encounters": [
        ENC("enc_hollow_pilgrims", 28.2, 8.4, 3.4,
            [("cr_hollow_spirit", 27.6, 9.4), ("cr_hollow_spirit", 29.8, 8.6), ("cr_hollow_wisp", 28.8, 10.6)]),
        ENC("enc_terrace_wisps", 35.2, 3.4, 2.8,
            [("cr_hollow_wisp", 34.4, 2.8), ("cr_hollow_wisp", 36.2, 3.8), ("cr_hollow_spirit", 35.6, 2.0)]),
        ENC("enc_keeper", 44.6, 8.4, 3.4,
            [("cr_hollow_keeper", 44.6, 9.8), ("cr_hollow_wisp", 43.0, 10.4), ("cr_hollow_wisp", 46.2, 10.2)],
            done="keeper_done", dialogue="dlg_keeper_ishiro"),
        ENC("enc_warden", 60.0, 7.6, 4.2, [("cr_hollow_warden", 62.0, 9.6)], require="reg_shrine_approach",
            done="warden_defeated", dialogue="dlg_warden_confront"),
    ],
    "chests": [
        CHEST("chest_shrine_terrace", 24.6, 1.6, loot="lt_chest_shrine"),
        CHEST("chest_keepers_offering", 48.6, 2.0, loot="lt_chest_offering", dc=16),
    ],
    "transitions": [TR("to_whisperwood", 0.7, 6.0, 1.4, 6.0, "whisperwood", "from_shrine", "Whisperwood")],
    "spawns": [SP("default", 3.6, 6.0), SP("from_whisperwood", 3.6, 6.0), SP("sanctum", 54.0, 6.0)],
    "regions": [
        REG("reg_shrine_approach", 8.0, 7.0, 14, 14, "The Old Lantern Shrine. Above you, a hundred dark lanterns "
                                                     "climb the cliffs like a stair of sleeping stars.", flag=True),
        REG("reg_pilgrim_terraces", 31.0, 7.0, 16, 14, "The Pilgrim Terraces. Grey shapes walk here, still on "
                                                       "pilgrimage, long after forgetting why."),
        REG("reg_keepers_hall", 44.6, 8.0, 10, 12, "The Keeper's Hall. Every lantern here has been pinched out by "
                                                   "hand."),
        REG("reg_sanctum", 62.0, 7.5, 16, 14, "The Sanctum of the Heart Lantern."),
    ],
    "ambient": {"embers": True, "mist": True, "timeOfDay": "dusk", "dayNightCycle": False,
                "ambientColor": "#c8b8e0", "ambientIntensity": 0.75},
    "restArea": True, "music": "music_shrine",
}

MAPS = {"lanternvale": LANTERNVALE, "whisperwood": WHISPERWOOD, "shrine": SHRINE}


# every dark spirit lantern gets a unique interact id so MapView.SetLanternLit(id) can relight it
DARK_TEXT = "A spirit-lantern, cold and dark. The stone still remembers being warm."
for _m in MAPS.values():
    _ids = set()
    for _i, _p in enumerate(_m["props"]):
        if _p["art"] in ("prop_spirit_lantern_dark", "prop_spirit_lantern"):
            if not _p.get("interact"):
                _p["interact"] = f"lantern_{_m['id']}_{_i}"
            assert _p["interact"] not in _ids, _p["interact"]
            _ids.add(_p["interact"])
            if not _p.get("text"):
                _p["text"] = DARK_TEXT if _p["art"].endswith("_dark") else "A spirit-lantern, still glowing honey-gold. For now."
