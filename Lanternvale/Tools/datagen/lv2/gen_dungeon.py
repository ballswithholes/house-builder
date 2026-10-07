"""The Root Hollows (56 x 40 cave): the entrance hall under Old Kusu, the Wisp Gallery (north), the Rootling Burrow
(south), the lantern-lit Root Grove with its pool and Hotaru, the Sentinel Gate and the Rootwarden's Hollow (east)."""
from gen_map_lv import P, crystal_light

VIOLET, ROSE, AMBER, TEAL = "#d8c4ff", "#ffc0e0", "#ffd9a0", "#bff6ea"


def crystal(x, y, tint, light_color, **kw):
    return P("prop_crystal_cluster", x, y, crystal_light(light_color), tint=tint, **kw)


def root_hollows():
    props = []
    A = props.append

    # ---- the entrance hall (west): Kusu's roots coming down through the roof, the old lamplighters' torches
    A(P("prop_root_arch", 7.6, 20.0, "root_arch_cave", text="The roots of Old Kusu come down through the roof here and braid into an arch. Two paper lanterns still hang from it, lit by someone, some time."))
    A(P("prop_torch_sconce", 4.6, 23.8, "sconce"))
    A(P("prop_torch_sconce", 4.6, 16.2, "sconce"))
    A(P("prop_root_column", 9.6, 27.6, text="A stone pillar the first lamplighters raised, long since swallowed by the roots of the tree above."))
    A(P("prop_root_column", 9.8, 11.8, flip=True))
    A(P("prop_stalagmite", 2.6, 28.6))
    A(P("prop_stalagmite", 5.4, 32.4))
    A(P("prop_stalagmite", 2.4, 11.4))
    A(P("prop_stalagmite", 5.8, 7.0))
    A(P("prop_rubble", 3.4, 36.0))
    A(P("prop_rubble", 3.0, 3.6))
    A(crystal(8.6, 34.6, VIOLET, "#c9b0ff"))
    A(P("prop_glow_mushroom", 8.8, 5.4, "glow_cave"))
    A(P("prop_bones", 11.6, 23.8))

    # ---- rock ribs: the Wisp Gallery wall (y 26.4) and the Burrow wall (y 13.4)
    for x in (18.6, 23.0, 27.4, 31.8):
        A(P("prop_cave_wall", x, 26.6, flip=(x > 25)))
        A(P("prop_cave_wall", x, 12.2, flip=(x < 25)))
    # ---- the Sentinel Gate: a rock mass across the cave at x ≈ 43.6 with an 8 m gap (y 16.8 - 24.8)
    y = 0.8
    while y <= 15.7:
        A(P("prop_cave_wall", 43.6 + (0.3 if int(y) % 2 else -0.3), y, flip=int(y) % 2 == 0))
        y += 1.0
    y = 25.9
    while y <= 39.4:
        A(P("prop_cave_wall", 43.6 + (0.3 if int(y) % 2 else -0.3), y, flip=int(y) % 2 == 0))
        y += 1.0
    A(P("prop_root_column", 41.2, 16.0, flip=True, text="Two great roots stand either side of the passage like door-wardens, wound with straw rope and paper charms."))
    A(P("prop_root_column", 41.2, 25.6))
    A(P("prop_torch_sconce", 40.4, 18.0, "sconce"))
    A(P("prop_torch_sconce", 40.4, 23.6, "sconce"))

    # ---- the Wisp Gallery (north)
    A(crystal(17.0, 36.8, VIOLET, "#c9b0ff"))
    A(crystal(21.2, 29.6, ROSE, "#ffb0d8"))
    A(crystal(30.0, 30.0, VIOLET, "#c9b0ff"))
    A(P("prop_glow_mushroom", 26.4, 38.4, "glow_cave"))
    A(P("prop_glow_mushroom", 13.6, 33.4, "glow_cave"))
    A(P("prop_stalagmite", 19.4, 38.6))
    A(P("prop_stalagmite", 34.6, 30.8))
    A(P("prop_stalagmite", 37.8, 35.4))
    A(P("prop_rubble", 36.4, 38.4))
    A(P("prop_bones", 22.6, 36.4))
    # the lamplighters' cache at the gallery's end
    A(P("prop_torch_sconce", 33.8, 38.4, "sconce"))
    A(P("prop_crate", 30.6, 38.6, text="A lamplighter's crate: spare wicks, a tinderbox, and a note in a careful hand — 'Kusu's lantern, checked. All well.' The date is ninety years old."))
    A(P("prop_barrel", 29.4, 37.6))

    # ---- the Rootling Burrow (south)
    A(P("prop_root_column", 29.4, 2.8, flip=True))
    A(crystal(16.8, 4.2, AMBER, "#ffc878"))
    A(crystal(34.8, 9.4, AMBER, "#ffc878"))
    A(P("prop_glow_mushroom", 21.2, 2.4, "glow_cave"))
    A(P("prop_rubble", 18.4, 10.6))
    A(P("prop_rubble", 36.8, 4.4))
    A(P("prop_bones", 25.8, 10.8))
    A(P("prop_bones", 33.8, 2.2))
    A(P("prop_stalagmite", 38.8, 11.0))
    A(P("prop_stalagmite", 13.4, 2.6))

    # ---- the Root Grove (centre): a pool under a shaft of root-light, giant glowing mushrooms, spirit lanterns
    A(P("prop_mushroom_giant", 37.0, 23.6, {"color": "#86f2d8", "radius": 3.0, "intensity": 0.6, "offset": [0.2, 2.45]}))
    A(P("prop_glow_mushroom", 25.0, 22.4, "glow_cave"))
    A(P("prop_glow_mushroom", 34.4, 15.2, "glow_cave"))
    A(P("prop_lilypads", 29.4, 23.2))
    A(P("prop_lilypads", 32.6, 22.0))
    A(P("prop_spirit_lantern", 23.6, 16.0, "spirit", text="A spirit-lantern, carried down here by the first lamplighters. It burns on Kusu's sap, they said."))
    A(P("prop_spirit_lantern", 35.6, 25.4, "spirit"))
    A(P("prop_spirit_lantern", 21.0, 24.2, "spirit"))
    A(crystal(19.0, 15.6, ROSE, "#ffb0d8"))
    A(P("prop_root_arch", 30.4, 16.2, "root_arch_cave", text="A root arch over the grove path. Its paper lanterns glow the colour of honey."))

    # ---- the Rootwarden's Hollow (east)
    A(P("prop_altar", 51.6, 28.6, "altar", text="The Root Lantern's altar: a vermilion runner, a bell, a bowl of very old honey and four candles that have never quite gone out."))
    A(P("prop_root_column", 48.0, 31.8))
    A(P("prop_root_column", 54.0, 32.0, flip=True))
    A(P("prop_root_column", 53.6, 8.4, flip=True))
    A(P("prop_root_arch", 51.6, 35.6, "root_arch_cave"))
    A(crystal(47.8, 9.0, AMBER, "#ffc878"))
    A(crystal(54.4, 14.0, ROSE, "#ffb0d8"))
    A(crystal(48.2, 36.8, VIOLET, "#c9b0ff"))
    A(P("prop_brazier", 47.6, 25.6, "brazier"))
    A(P("prop_brazier", 47.6, 15.6, "brazier"))
    A(P("prop_glow_mushroom", 54.6, 24.4, "glow_cave"))
    A(P("prop_bones", 50.2, 13.6))
    A(P("prop_stalagmite", 50.0, 3.6))
    A(P("prop_stalagmite", 54.8, 38.4))

    paths = [
        {"art": "decal_path_dirt", "width": 2.0, "points": [[0.0, 20.0], [6.0, 20.0], [12.0, 20.4], [18.0, 19.6], [24.0, 18.6], [30.0, 18.0], [36.0, 18.6], [41.0, 20.6], [46.0, 20.8], [50.0, 21.0]]},
        {"art": "decal_path_dirt", "width": 1.6, "points": [[12.4, 21.0], [14.4, 26.0], [16.8, 30.6], [21.6, 33.4], [27.6, 35.0], [31.4, 36.4]]},
        {"art": "decal_path_dirt", "width": 1.6, "points": [[12.4, 19.6], [14.4, 14.4], [17.4, 9.4], [22.0, 7.2], [27.6, 5.8], [31.0, 5.0]]},
        {"art": "decal_path_dirt", "width": 1.4, "points": [[46.0, 20.8], [50.0, 24.0], [51.6, 26.6]]},
    ]

    water = [{"closed": True, "halfWidth": 1.0, "points": [[26.8, 22.8], [28.0, 21.0], [30.6, 20.4], [33.4, 20.8], [34.6, 22.6], [33.6, 24.6], [30.4, 25.2], [27.8, 24.6]]}]

    encounters = [
        {"id": "enc_dg1_root_burrowers", "pos": [14.6, 20.2], "radius": 3.0,
         "enemies": [{"creature": "cr_dg1_rootling", "pos": [15.4, 21.2]}, {"creature": "cr_dg1_rootling", "pos": [15.8, 19.0]},
                     {"creature": "cr_dg1_rootling", "pos": [16.6, 20.4]}, {"creature": "cr_dg1_sapcaller", "pos": [17.6, 21.6]}]},
        {"id": "enc_dg1_wisp_gallery", "pos": [23.6, 32.8], "radius": 3.2,
         "enemies": [{"creature": "cr_dg1_root_wisp", "pos": [23.0, 34.0]}, {"creature": "cr_dg1_root_wisp", "pos": [25.0, 33.2]},
                     {"creature": "cr_dg1_greyed_kodama", "pos": [24.4, 35.2]}]},
        {"id": "enc_dg1_brute_den", "pos": [24.0, 7.0], "radius": 3.2,
         "enemies": [{"creature": "cr_dg1_rootling_brute", "pos": [23.4, 8.2]}, {"creature": "cr_dg1_rootling_brute", "pos": [25.4, 6.6]},
                     {"creature": "cr_dg1_sapcaller", "pos": [24.8, 4.8]}]},
        {"id": "enc_dg1_grove_ambush", "pos": [38.2, 19.0], "radius": 2.6, "hidden": True,
         "enemies": [{"creature": "cr_dg1_rootling", "pos": [37.6, 20.6]}, {"creature": "cr_dg1_rootling", "pos": [39.4, 17.6]},
                     {"creature": "cr_dg1_root_wisp", "pos": [39.6, 20.2]}]},
        {"id": "enc_dg1_sentinel_gate", "pos": [44.6, 20.8], "radius": 3.0,
         "enemies": [{"creature": "cr_dg1_root_sentinel", "pos": [45.2, 22.4]}, {"creature": "cr_dg1_root_sentinel", "pos": [45.2, 19.2]}]},
        {"id": "enc_dg1_rootwarden", "pos": [50.8, 21.0], "radius": 3.6,
         "enemies": [{"creature": "cr_dg1_rootwarden", "pos": [52.2, 22.0]}]},
    ]

    chests = [
        {"id": "chest_dg1_lamplighters_cache", "pos": [32.0, 36.6], "art": "prop_chest", "lootTable": "lt_dg1_chest_cache"},
        {"id": "chest_dg1_brute_hoard", "pos": [31.8, 5.0], "art": "prop_chest", "lootTable": "lt_dg1_chest_hoard",
         "lockCheck": {"skill": "SleightOfHand", "dc": 14}},
        {"id": "chest_dg1_root_lantern", "pos": [54.0, 28.8], "art": "prop_chest", "lootTable": "lt_dg1_chest_lantern", "requireFlag": "enc_enc_dg1_rootwarden"},
    ]

    regions = [
        {"id": "reg_dg1_entrance", "name": "Root Hall", "pos": [6.0, 20.0], "size": [10.0, 12.0],
         "text": "The Root Hollows. Kusu's roots come down through the roof like the pillars of a hall, and the air hums — one long, low, slightly off-key note."},
        {"id": "reg_dg1_grove", "name": "Root Grove", "pos": [30.0, 20.0], "size": [16.0, 11.0],
         "text": "A grove underground: a still pool, mushrooms as tall as a cottage door, and spirit-lanterns the first lamplighters carried down a hundred years ago. It is warm here, and it smells of honey."},
        {"id": "reg_dg1_wardens_hollow", "name": "Rootwarden's Hollow", "pos": [50.8, 20.8], "size": [9.0, 20.0],
         "text": "The heart of the roots. Something enormous breathes in the dark, and every breath makes the roots ache."},
    ]

    return {
        "id": "dgn_root_hollows",
        "name": "The Root Hollows",
        "subtitle": "Caves beneath Old Kusu's roots",
        "width": 56,
        "depth": 40,
        "skyTop": "#16141c",
        "skyBottom": "#2e2a36",
        "ground": "ground_cave",
        "groundTile": 8,
        "groundTint": "#a49c8e",
        "biome": "cave",
        "environment": "cave",
        "levelMin": 11,
        "levelMax": 13,
        "dungeon": True,
        "fill": 0.5,
        "paths": paths,
        "water": water,
        "props": props,
        "npcs": [{"npc": "dg1_hotaru", "pos": [25.2, 20.8]}],
        "encounters": encounters,
        "chests": chests,
        "transitions": [
            {"id": "to_lanternvale", "pos": [0.7, 20.0], "size": [1.4, 6.0], "targetMap": "lanternvale", "targetSpawn": "from_dgn_root_hollows", "label": "Lanternvale"}
        ],
        "spawns": [{"id": "default", "pos": [3.4, 20.0]}, {"id": "from_lanternvale", "pos": [3.4, 20.0]}],
        "regions": regions,
        "ambient": {"timeOfDay": "night", "dayNightCycle": False, "ambientColor": "#a8a0bc", "ambientIntensity": 0.6, "drips": True, "fireflies": True, "dust": True},
        "restArea": False,
        "music": "music_dungeon",
    }
