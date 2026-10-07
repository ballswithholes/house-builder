"""Lanternvale deepened (Docs/Expansion.md §1, §8 row lv): the original village strip (y 0-15) is kept exactly; this
adds the northern band — the west road to Amberfield, Old Kusu's roots and the Root Hollows mouth, the millpond and
Lantern Meadow, the Pipp orchard, the Hollyhock farm lane and Lantern Hill."""
import json, copy

# ---------------------------------------------------------------- prop helpers
COL = {
    "prop_tree_oak": (1.2, 0.7), "prop_tree_birch": (0.7, 0.4), "prop_tree_pine": (1.0, 0.6), "prop_tree_golden": (1.2, 0.8),
    "prop_bush_a": (1.2, 0.6), "prop_bush_b": (1.2, 0.6), "prop_rock_large": (2.0, 1.0), "prop_lamp_post": (0.4, 0.3),
    "prop_signpost": (0.4, 0.3), "prop_bench": (1.4, 0.4), "prop_barrel": (0.8, 0.5), "prop_crate": (0.9, 0.6),
    "prop_fence": (3.0, 0.35), "prop_hay": (1.2, 0.7), "prop_cart": (2.0, 1.0), "prop_stone_wall": (3.4, 0.6),
    "prop_veg_patch": (3.6, 1.8), "prop_washing_line": (3.4, 0.3), "prop_log": (2.2, 0.6), "prop_stump": (0.9, 0.5),
    "prop_cottage_a": (5.0, 2.2), "prop_cottage_b": (4.4, 2.0), "prop_cottage_c": (5.0, 2.2), "prop_spirit_lantern": (0.9, 0.5),
    "prop_spirit_statue": (0.8, 0.5), "prop_noticeboard": (1.6, 0.5), "prop_ruin_pillar": (1.1, 0.6), "prop_campfire": (1.0, 0.6),
    "prop_tent": (2.4, 1.2), "prop_well": (1.8, 0.9),
    # expansion props: the recommended colliders of Docs/ArtKeys.md
    "prop_root_column": (2.0, 1.3), "prop_glow_mushroom": (1.4, 0.9), "prop_farmhouse": (8.4, 5.2), "prop_beehive": (2.0, 0.9),
    "prop_scarecrow": (0.6, 0.4), "prop_cairn": (1.1, 0.8), "prop_standing_stone": (1.0, 0.7), "prop_cave_wall": (4.4, 1.3),
    "prop_stalagmite": (1.2, 0.8), "prop_crystal_cluster": (1.4, 0.9), "prop_torch_sconce": (0.6, 0.5), "prop_rubble": (1.8, 1.0),
    "prop_altar": (2.4, 1.2), "prop_mushroom_giant": (1.9, 1.1), "prop_brazier": (0.9, 0.7), "prop_treasure_pile": (2.0, 1.1), "prop_quarry_cart": (2.2, 1.1),
}
NOCOL = {"prop_mushrooms", "prop_rock_small", "prop_root_arch", "prop_lilypads", "prop_reeds", "prop_cattails", "prop_wheat",
         "prop_dock", "prop_bones", "prop_shrine_gate", "prop_ruin_arch", "decal_flowers", "decal_path_dirt"}
SWAY = {"prop_tree_oak", "prop_tree_birch", "prop_tree_pine", "prop_tree_golden", "prop_bush_a", "prop_bush_b",
        "prop_veg_patch", "prop_washing_line", "prop_wheat", "prop_reeds", "prop_cattails", "prop_scarecrow"}

LIGHTS = {
    "lamp": {"color": "#ffb46c", "radius": 4.0, "intensity": 0.9, "offset": [0, 2.3], "flicker": True, "nightOnly": True},
    "spirit": {"color": "#ffd27a", "radius": 3.5, "intensity": 0.9, "offset": [0, 1.6], "flicker": True},
    "cottage_a": {"color": "#ffb062", "radius": 3.5, "intensity": 0.7, "offset": [1.3, 2.4], "nightOnly": True},
    "cottage_b": {"color": "#ffb062", "radius": 3.5, "intensity": 0.7, "offset": [-1.0, 3.0], "nightOnly": True},
    "cottage_c": {"color": "#ffb062", "radius": 3.5, "intensity": 0.7, "offset": [1.5, 2.6], "nightOnly": True},
    "farmhouse": {"color": "#ffb062", "radius": 3.5, "intensity": 0.7, "offset": [-0.35, 2.45], "nightOnly": True},
    "root_arch": {"color": "#ffd28a", "radius": 3.0, "intensity": 0.6, "offset": [0, 2.7], "nightOnly": True},
    "glow": {"color": "#9ff3e4", "radius": 4.0, "intensity": 0.8, "offset": [0, 0.95], "nightOnly": True},
    "glow_cave": {"color": "#9ff3e4", "radius": 4.0, "intensity": 0.8, "offset": [0, 0.95]},
    "dock": {"color": "#ffd9a0", "radius": 3.5, "intensity": 0.8, "offset": [-0.95, 2.0], "nightOnly": True},
    "campfire": {"color": "#ffa050", "radius": 4.5, "intensity": 1.1, "offset": [0, 0.5], "flicker": True},
    "sconce": {"color": "#ffb46c", "radius": 4.0, "intensity": 0.9, "offset": [0, 2.3], "flicker": True},
    "brazier": {"color": "#ffb062", "radius": 5.0, "intensity": 1.0, "offset": [0, 1.35], "flicker": True},
    "altar": {"color": "#ffd28a", "radius": 3.5, "intensity": 0.8, "offset": [0, 1.25], "flicker": True},
    "root_arch_cave": {"color": "#ffd28a", "radius": 3.0, "intensity": 0.6, "offset": [0, 2.7]},
    "treasure": {"color": "#ffd88a", "radius": 3.5, "intensity": 0.7, "offset": [0, 0.6]},
}


def crystal_light(color):
    return {"color": color, "radius": 4.5, "intensity": 0.9, "offset": [0, 0.9]}


def P(art, x, y, light=None, scale=None, flip=False, sway=None, collider=True, **kw):
    p = {"art": art, "pos": [round(x, 2), round(y, 2)]}
    if scale is not None and abs(scale - 1) > 1e-3:
        p["scale"] = scale
    if flip:
        p["flip"] = True
    if collider and art in COL:
        w, h = COL[art]
        p["collider"] = {"w": w, "h": h}
    if light:
        p["light"] = copy.deepcopy(LIGHTS[light]) if isinstance(light, str) else light
    if (sway if sway is not None else art in SWAY):
        p["sway"] = True
    for k, v in kw.items():
        p[k] = v
    return p


# ---------------------------------------------------------------- Lanternvale north band
def lanternvale(base):
    m = copy.deepcopy(base)
    m["biome"] = "village"
    m["environment"] = "outdoor"
    m["levelMin"], m["levelMax"] = 1, 12
    m["fill"] = 0.32

    m["paths"] = [
        # the west road: from the plaza past Old Kusu, over the meadow to the Amberfield gate (to_amberfield, x 0.7 y 32)
        {"art": "decal_path_dirt", "width": 2.2, "points": [[18.6, 10.4], [18.2, 13.8], [16.8, 17.4], [13.4, 21.6], [9.4, 25.8], [5.4, 29.6], [2.0, 31.7], [0.0, 32.0]]},
        # the cross trail behind Kusu, past the millpond, to the meadow road
        {"art": "decal_path_dirt", "width": 1.7, "points": [[16.8, 17.4], [21.6, 18.8], [27.4, 19.6], [33.0, 21.2], [40.0, 22.6], [47.2, 23.2]]},
        # Kusu's roots: a narrow trail between the root arches to the mossy mound (the Root Hollows mouth)
        {"art": "decal_path_dirt", "width": 1.4, "points": [[25.6, 19.6], [24.6, 23.6], [23.8, 28.2], [23.6, 32.0], [23.6, 34.4]]},
        # the meadow road: between the inn and the smithy, north to Lantern Meadow
        {"art": "decal_path_dirt", "width": 2.0, "points": [[50.8, 7.8], [51.0, 13.6], [49.6, 18.2], [47.2, 23.2], [47.0, 28.0], [48.0, 32.6], [49.6, 35.4]]},
        # the orchard trail east to the farm lane
        {"art": "decal_path_dirt", "width": 1.7, "points": [[47.2, 23.2], [54.0, 23.8], [61.0, 24.2], [67.0, 24.6], [73.2, 24.8]]},
        # the farm lane: past the windmill to the Hollyhock farmhouse
        {"art": "decal_path_dirt", "width": 2.0, "points": [[75.6, 7.6], [75.8, 12.4], [75.0, 17.0], [73.6, 21.0], [73.2, 24.8], [73.8, 28.6], [76.6, 31.6]]},
        # Lantern Hill: a sheep track up behind the farm
        {"art": "decal_path_dirt", "width": 1.4, "points": [[76.6, 31.6], [80.8, 30.6], [83.6, 33.6], [85.2, 37.4], [86.0, 40.0]]},
        # the millpond walk to Teo's cottage and the ford
        {"art": "decal_path_dirt", "width": 1.4, "points": [[40.0, 22.6], [38.6, 25.6], [39.4, 27.0]]},
        {"art": "decal_path_dirt", "width": 1.4, "points": [[33.0, 21.2], [33.4, 26.0], [33.0, 31.0], [33.6, 36.4], [35.6, 40.6], [38.2, 41.0], [41.8, 41.6]]},
    ]

    m["water"] = [
        # the millpond (spring-fed; Teo's jetty reaches into it)
        {"closed": True, "halfWidth": 1.0, "points": [[35.2, 32.6], [36.2, 30.0], [39.2, 29.0], [42.6, 29.6], [44.2, 32.0], [43.4, 34.8], [40.4, 36.2], [37.0, 35.4]],
         "crossings": [{"pos": [39.4, 31.0], "size": [3.0, 4.6]}]},
        # the brook that fills it, down from the northern woods, forded below the alders
        {"halfWidth": 0.8, "points": [[38.4, 44.0], [38.0, 41.0], [39.0, 38.4], [39.6, 36.0]],
         "crossings": [{"pos": [38.1, 41.0], "size": [3.8, 2.2]}]},
    ]

    props = []
    A = props.append

    # ---- west road
    A(P("prop_lamp_post", 15.2, 19.8, "lamp"))
    A(P("prop_lamp_post", 11.0, 25.6, "lamp"))
    A(P("prop_lamp_post", 3.8, 34.2, "lamp"))
    A(P("prop_signpost", 7.6, 29.4, text="West: Amberfield Downs, and past them Brightwater on the river. North: Old Kusu's roots, the millpond and the orchards. East: home, and the inn."))
    A(P("prop_cairn", 2.6, 28.0, text="A milestone of stacked river stones. Carved into the top one: AMBERFIELD 3 LEAGUES. Someone has scratched underneath: 'it is more like 4'."))
    A(P("prop_tree_oak", 3.0, 21.0))
    A(P("prop_tree_birch", 1.2, 24.6))
    A(P("prop_bush_a", 6.6, 22.8))
    A(P("prop_bush_b", 14.2, 26.6))
    A(P("prop_tree_pine", 1.6, 38.6))
    A(P("prop_tree_pine", 5.2, 41.8))
    A(P("prop_tree_birch", 14.6, 36.0))
    A(P("prop_rock_large", 16.6, 30.4))
    A(P("prop_stone_wall", 9.0, 18.6))
    A(P("prop_stone_wall", 5.6, 18.4, flip=True))
    A(P("prop_hay", 7.2, 20.4))
    # the old waystation the Duskmane scouts have made camp in
    A(P("prop_ruin_pillar", 8.0, 36.4))
    A(P("prop_ruin_arch", 12.0, 37.6))
    A(P("prop_ruin_pillar", 13.8, 35.0))
    A(P("prop_campfire", 10.6, 33.2, "campfire"))
    A(P("prop_barrel", 8.6, 34.0, text="A cask of Lanternvale lamp oil, stamped with Tobben's lantern mark. It has been dragged here through the grass."))
    A(P("prop_barrel", 9.2, 35.2))
    A(P("prop_crate", 12.4, 34.6))
    A(P("prop_tent", 11.2, 39.6))
    A(P("prop_tree_oak", 17.4, 38.2))

    # ---- Old Kusu's roots and the Root Hollows mouth (north of the canopy, so the game camera sees it from every yaw)
    A(P("prop_root_arch", 24.6, 24.2, "root_arch", text="One of Old Kusu's great roots, arching out of the ground and back in again. Someone hung two paper lanterns from it long ago; they are still lit."))
    A(P("prop_root_arch", 23.8, 28.8, "root_arch", flip=True))
    A(P("prop_root_column", 18.8, 34.0, text="An old boundary stone, older than the village, gripped by four of Kusu's roots as if the tree were holding on to it. A straw rope circles it."))
    A(P("prop_root_column", 28.4, 33.8, flip=True))
    A(P("prop_glow_mushroom", 20.6, 31.0, "glow"))
    A(P("prop_glow_mushroom", 27.0, 30.6, "glow"))
    A(P("prop_mushrooms", 22.0, 21.6))
    A(P("prop_mushrooms", 28.4, 24.8))
    A(P("prop_log", 20.0, 26.8, interact="lv2_listening_root", dialogue="dlg_lv2_listening_root",
        text="A root of Old Kusu as thick as a barrel breaks the surface here, moss-soft and warm as a sleeping dog."))
    A(P("prop_spirit_statue", 27.4, 26.8, text="A little stone fox sits at the foot of the roots with its ear pressed to the ground, as if it is listening."))
    A(P("prop_spirit_lantern", 21.6, 24.0, "spirit", text="A spirit-lantern set among the roots, burning honey-gold."))
    # the mossy mound over the hidden mouth (gone once it is found)
    A(P("prop_bush_a", 22.4, 36.4, hideFlag="found_root_hollows", text="A mound of moss and fern between the roots. A cool draught breathes out of it."))
    A(P("prop_bush_b", 24.8, 36.6, hideFlag="found_root_hollows"))
    A(P("prop_spirit_lantern", 20.6, 35.6, "spirit", text="A stone lantern, burning honey-gold beside a mound of moss. Its twin stands on the other side. Odd place for a pair of lanterns — unless they mark a door."))
    A(P("prop_spirit_lantern", 26.8, 35.8, "spirit"))
    A(P("prop_rock_large", 19.6, 37.6))
    A(P("prop_rock_large", 27.8, 37.8))
    A(P("prop_tree_oak", 16.2, 32.4))
    A(P("prop_tree_birch", 30.8, 31.6))
    A(P("prop_tree_oak", 21.6, 41.8))
    A(P("prop_tree_pine", 27.0, 41.6))
    A(P("prop_bush_b", 17.4, 24.6))
    A(P("prop_stump", 30.6, 18.6))

    # ---- the millpond, Teo's cottage and the ford
    A(P("prop_dock", 39.4, 30.4, "dock", text="Teo's jetty. A tin of worms, a flask and a fishing rod that has clearly been mended more times than it has caught anything."))
    A(P("prop_lilypads", 41.8, 33.4))
    A(P("prop_lilypads", 37.2, 33.8))
    A(P("prop_lilypads", 42.8, 31.0))
    A(P("prop_reeds", 35.4, 34.4))
    A(P("prop_cattails", 36.0, 30.0))
    A(P("prop_reeds", 44.2, 33.6))
    A(P("prop_cattails", 43.8, 30.0))
    A(P("prop_cattails", 37.6, 37.4))
    A(P("prop_bench", 36.2, 27.6))
    A(P("prop_cottage_b", 31.4, 40.4, "cottage_b", text="Teo's cottage: a fishing net drying over the door and a carved wooden carp on the gable."))
    A(P("prop_veg_patch", 31.2, 37.4))
    A(P("prop_washing_line", 35.0, 38.2))
    A(P("prop_barrel", 35.2, 41.2))
    A(P("prop_lamp_post", 35.6, 25.4, "lamp"))
    A(P("prop_tree_birch", 41.0, 39.2))
    A(P("prop_tree_birch", 36.4, 43.0))

    # ---- Lantern Meadow (the green) and the Fernsby cottage
    A(P("prop_noticeboard", 52.6, 19.6, text="NORTH MEADOW NOTICES — Apples going missing, one bite out of each: see Hana Pipp at the orchard. Lamp oil going missing, ALL of it: see Tobben. The roots behind Kusu are singing: see Nell (she insists). Walk Night honey cakes: order from Dorrit."))
    A(P("prop_tree_golden", 50.6, 39.0, text="The Lantern Tree: the meadow's great golden beech. Every Walk Night the children hang a paper lantern on it for the Warden to find."))
    A(P("prop_bench", 48.0, 35.6))
    A(P("prop_bench", 53.4, 35.4))
    A(P("prop_spirit_lantern", 46.6, 33.0, "spirit"))
    A(P("prop_spirit_lantern", 54.6, 33.2, "spirit"))
    A(P("prop_cottage_c", 44.2, 41.4, "cottage_c", text="The Fernsbys' cottage. Seven pairs of boots by the door, all muddy, all small."))
    A(P("prop_veg_patch", 46.8, 37.6))
    A(P("prop_bush_a", 44.8, 26.8))
    A(P("prop_bush_b", 52.4, 27.6))
    A(P("prop_lamp_post", 49.6, 20.2, "lamp"))

    # ---- the Pipp orchard
    for i, x in enumerate([56.0, 59.6, 63.2, 66.8]):
        for j, y in enumerate([29.4, 33.2, 37.0]):
            if (i + j) % 2 == 0:
                A(P("prop_tree_oak", x + (0.3 if j == 1 else 0), y, scale=0.56))
            else:
                A(P("prop_tree_golden", x + (0.3 if j == 1 else 0), y, scale=0.46))
    for x in (55.4, 58.4):
        A(P("prop_fence", x, 26.6))
    for x in (64.2, 67.2):
        A(P("prop_fence", x, 26.6))
    A(P("prop_crate", 61.4, 30.8, interact="lv2_spilled_crate", dialogue="dlg_lv2_spilled_crate",
        text="An apple crate, tipped over. Every apple has exactly one neat bite out of it."))
    A(P("prop_crate", 57.6, 35.2))
    A(P("prop_cart", 62.0, 39.4))
    A(P("prop_hay", 54.4, 39.6))
    A(P("prop_cottage_a", 58.2, 41.4, "cottage_a", text="The Pipps' cottage, half buried in apple boughs. It smells of cider and woodsmoke."))
    A(P("prop_lamp_post", 60.0, 27.6, "lamp"))

    # ---- the Hollyhock farm and its lane
    A(P("prop_farmhouse", 77.6, 35.4, "farmhouse", text="Hollyhock Farm: whitewashed walls, a turf roof with hollyhocks growing out of it, and a barn that leans companionably on the house."))
    A(P("prop_beehive", 69.4, 30.4, text="Ama Hollyhock's skeps. The bees are busy, and quite firm about it."))
    A(P("prop_beehive", 69.6, 33.8))
    A(P("prop_hay", 81.6, 31.0))
    A(P("prop_hay", 82.8, 29.6))
    A(P("prop_cart", 71.4, 36.6))
    A(P("prop_barrel", 73.0, 37.8))
    A(P("prop_lamp_post", 74.6, 28.4, "lamp"))
    A(P("prop_veg_patch", 70.2, 19.8, text="Pumpkins, fat and smug."))
    A(P("prop_veg_patch", 70.2, 22.0))
    for x in (81.6, 84.6, 87.6):
        A(P("prop_fence", x, 17.6))
    for x in (80.8, 84.4, 88.0):
        for y in (19.6, 21.8, 24.0, 26.2):
            A(P("prop_wheat", x, y))
    A(P("prop_scarecrow", 84.6, 27.8, text="A scarecrow in a patched coat. The crow on its arm is real, and unimpressed."))
    A(P("prop_washing_line", 82.6, 39.6))
    A(P("prop_tree_oak", 68.8, 40.4))
    A(P("prop_tree_pine", 72.4, 42.8))
    A(P("prop_tree_birch", 78.6, 42.4))

    # ---- Lantern Hill (the lookout)
    A(P("prop_bench", 86.2, 41.8, text="A bench someone carried all the way up here, for the view."))
    A(P("prop_standing_stone", 88.6, 40.4, text="An old standing stone. Carved on it: 'From here you can see everything that matters.' Underneath, smaller: 'and the inn'."))
    A(P("prop_tree_birch", 83.0, 42.8))
    A(P("prop_rock_large", 88.6, 36.2))
    A(P("prop_bush_a", 82.6, 36.4))
    A(P("prop_cairn", 87.8, 37.4, text="A little cairn of hill stones. Everyone who climbs Lantern Hill adds one, and makes a wish on it."))
    A(P("prop_mushrooms", 85.0, 43.0))

    # ---- the northern treeline
    for art, x, y in [("prop_tree_pine", 10.4, 43.0), ("prop_tree_oak", 14.8, 42.4), ("prop_tree_pine", 24.8, 42.8), ("prop_tree_birch", 29.4, 43.4),
                      ("prop_tree_pine", 47.6, 43.4), ("prop_tree_oak", 54.6, 43.2), ("prop_tree_pine", 64.4, 43.0), ("prop_tree_pine", 90.0, 43.4)]:
        A(P(art, x, y))

    # scatter flowers where the meadow is open
    for x, y in [(8.0, 22.6), (15.0, 27.0), (44.0, 24.6), (55.0, 31.4), (65.0, 38.6), (79.0, 27.4), (86.0, 34.6), (12.6, 41.0), (46.4, 38.6)]:
        A({"art": "decal_flowers", "pos": [x, y]})

    m["props"] = base["props"] + props

    m["npcs"] = base["npcs"] + [
        {"npc": "lv2_hana_pipp", "pos": [58.4, 25.4]},
        {"npc": "lv2_teo", "pos": [39.4, 32.4], "flip": True},
        {"npc": "lv2_ama_hollyhock", "pos": [72.0, 30.2]},
        {"npc": "lv2_sprout", "pos": [65.0, 31.4], "flip": True, "requireFlag": "lv2_rootling_friend"},
    ]

    m["encounters"] = base["encounters"] + [
        {"id": "enc_lv2_orchard_rootlings", "pos": [61.6, 34.6], "radius": 3.0, "requireFlag": "lv2_orchard_hunt",
         "enemies": [{"creature": "cr_lv2_orchard_rootling", "pos": [60.8, 35.6]}, {"creature": "cr_lv2_orchard_rootling", "pos": [62.6, 35.2]},
                     {"creature": "cr_lv2_rootling_sapling", "pos": [61.6, 36.4]}]},
        {"id": "enc_lv2_duskmane_scouts", "pos": [10.4, 35.4], "radius": 3.2, "requireFlag": "lv2_oil_hunt", "doneFlag": "lv2_scouts_dealt",
         "dialogue": "dlg_lv2_duskmane_scouts",
         "enemies": [{"creature": "cr_lv2_duskmane_scout", "pos": [9.6, 36.2]}, {"creature": "cr_lv2_duskmane_scout", "pos": [11.6, 35.8]},
                     {"creature": "cr_lv2_duskmane_lookout", "pos": [10.8, 37.6]}]},
        {"id": "enc_lv2_wheat_boars", "pos": [84.4, 22.6], "radius": 3.2,
         "enemies": [{"creature": "cr_lv2_wheat_tusker", "pos": [83.6, 23.4]}, {"creature": "cr_lv2_wheat_tusker", "pos": [85.6, 22.2]},
                     {"creature": "cr_lv2_bristlesow", "pos": [84.8, 24.4]}]},
    ]

    m["chests"] = base["chests"] + [
        {"id": "chest_lv2_hill_basket", "pos": [84.6, 41.2], "art": "prop_chest", "lootTable": "lt_lv2_hill_basket"},
        {"id": "chest_lv2_root_hollow", "pos": [17.6, 29.4], "art": "prop_chest", "lootTable": "lt_lv2_root_hollow",
         "lockCheck": {"skill": "SleightOfHand", "dc": 12}},
    ]

    for t in m["transitions"]:
        if t["id"] == "to_dgn_root_hollows":
            t["pos"] = [23.6, 35.0]
    for s in m["spawns"]:
        if s["id"] == "from_dgn_root_hollows":
            s["pos"] = [23.6, 32.4]

    m["regions"] = base["regions"] + [
        {"id": "reg_lv2_kusu_roots", "name": "Kusu's Roots", "pos": [23.6, 30.0], "size": [12.0, 10.0],
         "text": "Behind Old Kusu the ground rises in great knuckled roots, moss-soft and warm. The air hums, very faintly, like a held note.",
         "check": {"skill": "Perception", "dc": 13}, "checkFlag": "found_root_hollows",
         "successText": "Under the hum there is a draught: cool air breathing out of a mound of moss between the roots. Something down there is hollow, and lit.",
         "failText": "You listen for a while. Roots, moss, one bee. Nothing else... probably."},
        {"id": "reg_lv2_west_road", "name": "West Road", "pos": [9.0, 27.0], "size": [12.0, 10.0],
         "text": "The west road. It climbs out of the valley towards Amberfield Downs, where the grass turns gold."},
        {"id": "reg_lv2_oil_tracks", "pos": [13.4, 21.8], "size": [6.0, 5.0],
         "text": "Lamp oil darkens the road here, as if a cask was rolled through it. Big four-toed paw prints lead off north-west, towards the old waystation."},
        {"id": "reg_lv2_millpond", "name": "Millpond", "pos": [39.4, 32.0], "size": [12.0, 9.0],
         "text": "The millpond. Dragonflies, lily pads, and Teo, who has been fishing here since before the mill fell down."},
        {"id": "reg_lv2_lantern_meadow", "name": "Lantern Meadow", "pos": [50.4, 35.0], "size": [10.0, 10.0],
         "text": "Lantern Meadow, the village green of the north quarter, under the great golden Lantern Tree."},
        {"id": "reg_lv2_orchard", "name": "Pipp Orchard", "pos": [61.4, 33.4], "size": [14.0, 13.0],
         "text": "The Pipp orchard: crooked apple trees heavy with red fruit, and an awful lot of bitten apples on the grass."},
        {"id": "reg_lv2_farm", "name": "Hollyhock Farm", "pos": [79.0, 27.0], "size": [18.0, 18.0],
         "text": "Hollyhock Farm. Wheat to the east, bees to the west, and a scarecrow that has given up."},
        {"id": "reg_lv2_lantern_hill", "name": "Lantern Hill", "pos": [86.0, 40.0], "size": [8.0, 7.0],
         "text": "Lantern Hill. The whole valley folds out below you like a quilt: the square, the inn's smoke, Whisperwood dark in the east. And far to the north, past the mountains, a thin grey smudge hangs on the sky like a thumbprint of ash."},
    ]
    return m
