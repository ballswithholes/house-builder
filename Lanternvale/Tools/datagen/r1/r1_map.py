"""The Hollow Heart (raid_hollow_heart): a corrupted spirit-grove cavern under Mirefen, 110 x 60 m.

Layout (x east, y north/away from the camera):
  west column    the Lantern Stair: the way in from Mirefen, Quill's camp and Hinoki's altar (north)
  south band     the Root Gallery (trash) -> Thornmaw's Den (boss 1)
  gate A         black thorns at (47, 30) wither when Thornmaw falls
  north band     the Weeping Pools (trash, the Weeping Twins between the pools, a hidden offering alcove)
  gate B         thorns at (68, 30): the Twins
  south-east     Mire Hollow (trash, Mother Mire's kitchen between the bogs)
  gate C         thorns at (96.5, 30): Mother Mire
  north-east     the Heart Chamber (trash, the Hollow Heart before the old heart lantern)
Rock ribs (prop_cave_wall) partition the rooms; the gates are the only ways on.
"""

W, D = 110, 60
MAP = "raid_hollow_heart"

# encounter done flags (the default "enc_<id>": quest Defeat objectives pull them, thorn gates hide on them)
F_THORNMAW = "enc_enc_r1_thornmaw"
F_TWINS = "enc_enc_r1_twins"
F_MIRE = "enc_enc_r1_mother_mire"
F_HEART = "enc_enc_r1_hollow_heart"

# canonical colliders and lights (Docs/ArtKeys.md 3D-only blocks; the same for every map, scaled or not)
COL = {
    "prop_cave_wall": (4.4, 1.3), "prop_stalagmite": (1.2, 0.8), "prop_crystal_cluster": (1.4, 0.9),
    "prop_glow_mushroom": (1.4, 0.9), "prop_root_column": (2.0, 1.3), "prop_thorn_wall": (4.4, 1.2),
    "prop_hollow_heart_core": (5.4, 3.4), "prop_torch_sconce": (0.6, 0.5), "prop_altar": (2.4, 1.2),
    "prop_tent": (2.4, 1.2), "prop_campfire": (1.0, 0.6), "prop_crate": (0.9, 0.6), "prop_barrel": (0.8, 0.5),
    "prop_spirit_lantern": (0.9, 0.5), "prop_spirit_lantern_dark": (0.9, 0.5), "prop_spirit_statue": (0.8, 0.5),
    "prop_blight_crystal": (1.2, 0.7), "prop_rock_large": (2.0, 1.0), "prop_log": (2.2, 0.6),
    "prop_bonepile": (1.6, 1.2), "prop_willow": (1.2, 0.8), "prop_mangrove": (2.2, 1.8), "prop_fen_lantern": (0.4, 0.4),
    "prop_mire_totem": (0.8, 0.6), "prop_sunken_statue": (2.8, 1.7), "prop_mushroom_giant": (1.9, 1.1),
    "prop_tree_dead": (1.0, 0.6), "prop_rubble": (1.8, 1.0), "prop_shop_stall": (3.0, 1.1), "prop_noticeboard": (1.6, 0.5),
    "prop_bench": (1.4, 0.4), "prop_banner": (0.4, 0.3), "prop_brazier": (0.9, 0.7), "prop_treasure_pile": (2.0, 1.1),
}
NO_COL = {"prop_root_arch", "prop_bones", "prop_rock_small", "prop_mushrooms", "prop_reeds", "prop_cattails", "prop_lilypads",
          "decal_blight", "decal_path_dirt", "decal_flowers"}


def L(color, radius, intensity, ox, oy, flicker=False, night=False):
    d = {"color": color, "radius": radius, "intensity": intensity, "offset": [ox, oy]}
    if flicker:
        d["flicker"] = True
    if night:
        d["nightOnly"] = True
    return d


LIGHT = {
    "prop_crystal_cluster": lambda c="#c9a8ff": L(c, 4.5, 0.9, 0, 0.9),
    "prop_glow_mushroom": lambda c="#9ff3e4": L(c, 4, 0.8, 0, 0.95),
    "prop_root_arch": lambda c="#ffd28a": L(c, 3, 0.6, 0, 2.7),
    "prop_hollow_heart_core": lambda c="#ff7ab0": L(c, 8, 1.2, 0, 3.2),
    "prop_torch_sconce": lambda c="#ffb46c": L(c, 4, 0.9, 0, 2.3, flicker=True),
    "prop_altar": lambda c="#ffd28a": L(c, 3.5, 0.8, 0, 1.25, flicker=True),
    "prop_campfire": lambda c="#ffa050": L(c, 4.5, 1.1, 0, 0.5, flicker=True),
    "prop_spirit_lantern": lambda c="#ffd27a": L(c, 3.5, 0.9, 0, 1.6, flicker=True),
    "prop_spirit_lantern_dark": lambda c="#ff9a6a": L(c, 3.0, 0.5, 0, 1.45, flicker=True),
    "prop_blight_crystal": lambda c="#b49cff": L(c, 2.6, 0.6, 0, 1.0),
    "prop_fen_lantern": lambda c="#e8f0b0": L(c, 4, 0.9, 0.55, 1.78, flicker=True),
    "prop_mire_totem": lambda c="#7cf0a0": L(c, 2.5, 0.6, 0, 2.45),
    "prop_mushroom_giant": lambda c="#86f2d8": L(c, 3, 0.6, 0.2, 2.45, night=True),
    "prop_mushrooms": lambda c="#8ff0d2": L(c, 2.0, 0.6, 0, 0.3),
    "prop_brazier": lambda c="#ffb062": L(c, 5, 1.0, 0, 1.35, flicker=True),
}

VIOLET, ROSE, ICE = "#d8c4ff", "#ffc0e0", "#cfefff"

props = []


def P(art, x, y, scale=1.0, lit=False, color=None, tint="", flip=False, sway=False, **kw):
    """One prop with its canonical collider; lit=True adds its canonical light (color overrides the light colour)."""
    p = {"art": art, "pos": [round(x, 2), round(y, 2)]}
    if scale != 1.0:
        p["scale"] = scale
    if flip:
        p["flip"] = True
    if art in COL:
        w, h = COL[art]
        p["collider"] = {"w": w, "h": h}
    if lit:
        p["light"] = LIGHT[art](color) if color else LIGHT[art]()
    if tint:
        p["tint"] = tint
    if sway:
        p["sway"] = True
    for k, v in kw.items():
        if v not in (None, ""):
            p[k] = v
    props.append(p)
    return p


# ----------------------------------------------------------------------------------------------------------- rock ribs

def rib_v(x, y0, y1, step=1.2, seed=0):
    """A north-south ridge of stacked rock ribs (every piece crosses the line x; a root column now and then)."""
    y, i = y0, 0
    while y <= y1 + 1e-6:
        jitter = ((i * 37 + seed) % 5 - 2) * 0.12
        if (i + seed) % 6 == 3:
            P("prop_root_column", x + jitter * 0.5, y, flip=(i % 2 == 0))
        else:
            P("prop_cave_wall", x + jitter, y, flip=(i % 2 == 1))
        y += step
        i += 1


def rib_h(xs, y):
    for i, x in enumerate(xs):
        P("prop_cave_wall", x, y + (0.15 if i % 2 else -0.1), flip=(i % 2 == 1))


def thorn_gate(xs, y, flag):
    for i, x in enumerate(xs):
        P("prop_thorn_wall", x, y, flip=(i % 2 == 1), hideFlag=flag)
    mid = sum(xs) / len(xs)
    P("prop_root_arch", mid, y + 0.25, lit=True)


def build_props():
    props.clear()
    # ---- partitions: the Lantern Stair | the Pools, the Den | Mire Hollow, the Pools | the Heart Chamber
    rib_v(22.6, 31.0, 59.4, seed=1)
    rib_v(63.4, 0.6, 29.4, seed=2)
    rib_v(72.8, 31.0, 59.4, seed=4)
    # ---- the long rib between the south and north bands, with three thorn gates
    rib_h([24.8, 28.6, 32.4, 36.2, 40.3], 30.2)
    thorn_gate([44.7, 49.3], 30.2, F_THORNMAW)
    rib_h([53.7, 57.5, 61.3], 30.2)
    thorn_gate([65.7, 70.1], 30.2, F_TWINS)
    rib_h([74.5, 78.3, 82.1, 85.9, 89.8], 30.2)
    thorn_gate([94.2, 98.8], 30.2, F_MIRE)
    rib_h([103.2, 107.9], 30.2)

    # ================================================================== the Lantern Stair (west column)
    # the way in: torches either side of the passage to Mirefen
    P("prop_torch_sconce", 2.6, 25.8, lit=True)
    P("prop_torch_sconce", 2.6, 34.2, lit=True)
    P("prop_stalagmite", 1.6, 22.6)
    P("prop_stalagmite", 1.8, 37.6, flip=True)
    P("prop_glow_mushroom", 1.8, 40.6, lit=True)
    P("prop_crystal_cluster", 1.6, 19.4, lit=True, color="#c9a8ff", tint=VIOLET)
    # the front of the stair: flowstone, roots and old bones by the pit
    for x, y, fl in [(4.6, 3.2, False), (8.8, 1.6, True), (13.4, 3.0, False), (17.4, 1.8, True), (21.6, 4.6, False)]:
        P("prop_stalagmite", x, y, flip=fl)
    P("prop_root_column", 6.2, 8.4)
    P("prop_rock_large", 11.2, 6.8)
    P("prop_glow_mushroom", 15.4, 8.2, lit=True)
    P("prop_crystal_cluster", 19.6, 9.6, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_bones", 9.6, 10.6)
    P("prop_rock_small", 13.2, 11.8)
    P("prop_mushrooms", 3.4, 12.4, lit=True)
    # Quill's camp (north of the way in)
    P("prop_tent", 7.6, 47.6)
    P("prop_campfire", 11.0, 41.4, lit=True)
    P("prop_log", 13.6, 40.2)
    P("prop_crate", 5.2, 44.2)
    P("prop_crate", 5.9, 43.1, flip=True)
    P("prop_barrel", 15.2, 46.4)
    P("prop_barrel", 16.1, 45.6)
    P("prop_shop_stall", 13.8, 48.8)
    P("prop_noticeboard", 17.6, 41.2, interact="r1_raid_roster",
      text="RAID ROSTER. Bring a healer. Bring two. Sign below. (Somebody has signed 'Sir Wadsworth' in a very wobbly hand, with a heron's footprint.)")
    P("prop_banner", 3.6, 41.0)
    P("prop_torch_sconce", 18.8, 47.4, lit=True)
    P("prop_stalagmite", 19.8, 51.6)
    P("prop_rock_large", 15.8, 53.6)
    # Hinoki's altar (the north-west corner, under the old roots)
    P("prop_altar", 5.0, 56.8, lit=True)
    P("prop_spirit_lantern", 2.6, 55.6, lit=True)
    P("prop_spirit_lantern", 7.6, 55.6, lit=True, flip=True)
    P("prop_spirit_statue", 9.8, 57.6)
    P("prop_root_column", 13.2, 57.4)
    P("prop_glow_mushroom", 1.6, 50.4, lit=True)
    P("prop_glow_mushroom", 17.6, 57.8, lit=True)
    P("prop_mushrooms", 10.6, 53.0, lit=True)

    # ================================================================== the Root Gallery (south band, west)
    P("prop_root_column", 24.6, 9.6)
    P("prop_root_column", 39.4, 24.2, flip=True)
    P("prop_root_arch", 32.0, 26.6, lit=True)
    for x, y, fl in [(27.0, 2.6, False), (30.4, 1.4, True), (34.2, 3.0, False), (38.8, 1.6, True), (42.6, 3.4, False)]:
        P("prop_stalagmite", x, y, flip=fl)
    P("prop_glow_mushroom", 37.6, 27.6, lit=True)
    P("prop_glow_mushroom", 41.8, 9.4, lit=True)
    P("prop_crystal_cluster", 28.2, 27.2, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_crystal_cluster", 33.6, 9.8, lit=True, color="#ff9ad0", tint=ROSE)
    P("prop_bones", 30.8, 11.2)
    P("prop_bones", 38.2, 12.8)
    P("prop_rock_large", 40.6, 5.2)
    P("prop_rock_small", 35.4, 6.0)
    P("prop_mushrooms", 26.6, 13.0, lit=True)
    # the ash drift: grey roots, dry ash and a cinder that never went out (the lore quest's first clue)
    P("decal_blight", 36.0, 27.4, scale=1.3)
    P("prop_blight_crystal", 35.4, 27.6, lit=True, color="#ffb27a", tint="#d9d2cc", interact="r1_ash_drift",
      dialogue="dlg_r1_ash_drift")
    P("prop_rock_small", 34.0, 28.4)
    # a drowned light in the thorns (Light Them Home #1)
    P("prop_thorn_wall", 25.6, 6.6, tint="#c8b8d8")
    P("prop_spirit_lantern_dark", 25.8, 5.0, lit=True, interact="r1_captive_light_1", dialogue="dlg_r1_captive_light_1",
      hideFlag="r1_light_1")
    P("prop_spirit_lantern", 25.8, 5.0, lit=True, requireFlag="r1_light_1")

    # ================================================================== Thornmaw's Den (south band, centre)
    # thorns and bones ring the den; the floor round the beast stays open for the fight
    P("prop_thorn_wall", 45.6, 2.4)
    P("prop_thorn_wall", 55.2, 1.8, flip=True)
    P("prop_thorn_wall", 59.4, 26.0, flip=True)
    P("prop_bonepile", 48.8, 5.6)
    P("prop_bonepile", 57.6, 6.8, flip=True)
    P("prop_bones", 44.2, 19.6)
    P("prop_bones", 60.0, 15.6)
    P("prop_bones", 52.4, 24.4)
    P("prop_crystal_cluster", 44.6, 9.2, lit=True, color="#ff9ad0", tint=ROSE)
    P("prop_crystal_cluster", 60.2, 10.2, lit=True, color="#ff9ad0", tint=ROSE)
    P("prop_crystal_cluster", 56.4, 23.2, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_glow_mushroom", 44.4, 22.8, lit=True)
    P("prop_glow_mushroom", 61.0, 4.0, lit=True)
    P("prop_crystal_cluster", 49.6, 20.8, lit=True, color="#ff9ad0", tint=ROSE)
    P("prop_root_column", 43.2, 25.8, flip=True)
    P("prop_tree_dead", 50.6, 3.4, tint="#9a8aa8")
    P("prop_mushrooms", 54.8, 9.2, lit=True, color="#ff9ad0")
    P("prop_spirit_lantern_dark", 59.6, 24.4, lit=True, interact="r1_captive_light_2", dialogue="dlg_r1_captive_light_2",
      hideFlag="r1_light_2")
    P("prop_spirit_lantern", 59.6, 24.4, lit=True, requireFlag="r1_light_2")
    # the gate: a root arch the thorns are woven through
    P("prop_stalagmite", 42.0, 28.0)
    P("prop_stalagmite", 52.6, 28.2, flip=True)

    # ================================================================== the Weeping Pools (north band, west)
    P("prop_willow", 31.0, 56.0, tint="#b6a8cc", sway=True)
    P("prop_willow", 65.4, 57.8, tint="#b6a8cc", sway=True, flip=True)
    P("prop_lilypads", 34.4, 47.0)
    P("prop_lilypads", 61.4, 51.4, flip=True)
    P("prop_lilypads", 49.4, 55.6)
    # drowned lanterns round the pools (they burn again when the grove is relit)
    for i, (x, y) in enumerate([(27.6, 41.2), (42.0, 43.2), (44.4, 53.2), (54.6, 52.8), (56.6, 45.0), (69.6, 46.4)]):
        P("prop_spirit_lantern_dark", x, y, lit=True, flip=(i % 2 == 1), hideFlag="r1_grove_relit")
        P("prop_spirit_lantern", x, y, lit=True, flip=(i % 2 == 1), requireFlag="r1_grove_relit")
    P("prop_crystal_cluster", 25.8, 35.6, lit=True, color="#9fd0ff", tint=ICE)
    P("prop_crystal_cluster", 40.6, 58.0, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_crystal_cluster", 57.0, 58.4, lit=True, color="#9fd0ff", tint=ICE)
    P("prop_crystal_cluster", 45.0, 44.6, lit=True, color="#9fd0ff", tint=ICE)
    P("prop_crystal_cluster", 53.4, 44.4, lit=True, color="#ff9ad0", tint=ROSE)
    P("prop_glow_mushroom", 25.2, 47.0, lit=True)
    P("prop_glow_mushroom", 43.0, 57.6, lit=True)
    P("prop_glow_mushroom", 70.4, 36.6, lit=True)
    P("prop_glow_mushroom", 57.6, 35.2, lit=True)
    P("prop_root_column", 37.2, 33.6)
    P("prop_root_column", 56.6, 33.4, flip=True)
    P("prop_stalagmite", 26.2, 32.6)
    P("prop_stalagmite", 69.8, 54.6)
    P("prop_rock_large", 52.6, 58.4)
    P("prop_bones", 40.4, 38.6)
    P("prop_spirit_statue", 49.4, 58.4)
    # the hidden offerings (a Perception check in the corner, or Hinoki's hint)
    P("prop_thorn_wall", 27.4, 56.8, tint="#c8b8d8", hideFlag="r1_found_offerings")
    P("prop_spirit_statue", 25.2, 58.6, requireFlag="r1_found_offerings")
    P("prop_mushrooms", 29.6, 58.8, lit=True, requireFlag="r1_found_offerings")
    # a drowned light in the thorns (#3)
    P("prop_thorn_wall", 60.2, 36.6, flip=True, tint="#c8b8d8")
    P("prop_spirit_lantern_dark", 60.4, 35.0, lit=True, interact="r1_captive_light_3", dialogue="dlg_r1_captive_light_3",
      hideFlag="r1_light_3")
    P("prop_spirit_lantern", 60.4, 35.0, lit=True, requireFlag="r1_light_3")

    # ================================================================== Mire Hollow (south band, east)
    P("prop_mangrove", 67.4, 9.6)
    P("prop_mangrove", 107.0, 17.4, flip=True)
    P("prop_willow", 104.2, 27.0, tint="#a8b89c", sway=True)
    for x, y, art in [(69.0, 3.4, "prop_reeds"), (72.2, 10.6, "prop_cattails"), (79.2, 11.6, "prop_reeds"), (84.0, 6.0, "prop_cattails"),
                      (76.4, 22.0, "prop_reeds"), (85.6, 25.6, "prop_cattails"), (98.2, 10.6, "prop_reeds"), (104.4, 12.8, "prop_cattails"),
                      (108.8, 4.2, "prop_reeds"), (97.6, 2.2, "prop_cattails")]:
        P(art, x, y, sway=True)
    P("prop_lilypads", 77.0, 6.4)
    P("prop_lilypads", 103.4, 7.0, flip=True)
    P("prop_lilypads", 81.0, 24.8)
    # the coven's totems ring Mother Mire's kitchen
    for x, y in [(79.6, 13.6), (87.0, 6.4), (94.6, 13.2), (90.6, 21.2), (82.4, 20.4)]:
        P("prop_mire_totem", x, y, lit=True, sway=True)
    P("prop_campfire", 87.0, 9.8, lit=True, color="#8cf09a")
    P("prop_barrel", 84.4, 8.6)
    P("prop_crate", 89.8, 8.4, flip=True)
    P("prop_bonepile", 92.6, 4.4)
    P("prop_fen_lantern", 68.6, 23.6, lit=True)
    P("prop_fen_lantern", 75.4, 17.0, lit=True)
    P("prop_fen_lantern", 96.6, 24.2, lit=True)
    P("prop_fen_lantern", 101.6, 15.8, lit=True)
    P("prop_mushroom_giant", 66.6, 18.0, lit=True)
    P("prop_mushroom_giant", 99.6, 27.6, lit=True, flip=True)
    P("prop_mushroom_giant", 93.4, 1.8, lit=True)
    P("prop_sunken_statue", 80.6, 2.6)
    P("prop_tree_dead", 73.2, 26.6, tint="#8c9a80")
    P("prop_tree_dead", 108.6, 9.8, tint="#8c9a80", flip=True)
    P("prop_glow_mushroom", 69.6, 14.6, lit=True)
    P("prop_crystal_cluster", 100.2, 21.0, lit=True, color="#a8f0b0", tint="#d6f5c8")
    # a drowned light in the thorns (#4)
    P("prop_thorn_wall", 106.6, 23.4, tint="#c8b8d8")
    P("prop_spirit_lantern_dark", 106.6, 21.6, lit=True, interact="r1_captive_light_4", dialogue="dlg_r1_captive_light_4",
      hideFlag="r1_light_4")
    P("prop_spirit_lantern", 106.6, 21.6, lit=True, requireFlag="r1_light_4")

    # ================================================================== the Heart Chamber (north band, east)
    P("prop_hollow_heart_core", 91.0, 56.6, lit=True, interact="r1_heart_lantern", dialogue="dlg_r1_heart_lantern")
    P("prop_root_column", 82.4, 53.6)
    P("prop_root_column", 99.6, 53.6, flip=True)
    P("prop_thorn_wall", 79.6, 57.8)
    P("prop_thorn_wall", 102.4, 57.8, flip=True)
    P("prop_crystal_cluster", 84.2, 46.0, lit=True, color="#ff7ab0", tint=ROSE)
    P("prop_crystal_cluster", 97.8, 46.0, lit=True, color="#ff7ab0", tint=ROSE)
    P("prop_crystal_cluster", 86.2, 57.8, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_crystal_cluster", 95.8, 57.8, lit=True, color="#c9a8ff", tint=VIOLET)
    P("prop_glow_mushroom", 76.6, 35.8, lit=True)
    P("prop_glow_mushroom", 108.2, 38.0, lit=True)
    P("prop_glow_mushroom", 78.0, 49.0, lit=True)
    P("prop_glow_mushroom", 106.6, 49.4, lit=True)
    P("prop_stalagmite", 75.6, 58.4)
    P("prop_stalagmite", 108.6, 58.4, flip=True)
    P("prop_stalagmite", 108.8, 32.4)
    P("prop_bones", 86.4, 41.0)
    P("prop_bones", 101.2, 44.0)
    P("prop_bonepile", 104.6, 33.6, flip=True)
    for i, (x, y) in enumerate([(78.6, 42.2), (103.8, 41.8), (84.6, 34.0), (88.6, 52.8), (93.4, 52.8)]):
        P("prop_spirit_lantern_dark", x, y, lit=True, flip=(i % 2 == 1), hideFlag=F_HEART if i >= 3 else "r1_grove_relit")
        P("prop_spirit_lantern", x, y, lit=True, flip=(i % 2 == 1), requireFlag=F_HEART if i >= 3 else "r1_grove_relit")
    dressing()
    # the husk of the Hollow seed, left in the roots when the heart stops (the lore quest)
    P("prop_blight_crystal", 91.0, 52.4, lit=True, color="#ffb27a", tint="#bdb5ad", interact="r1_seed_husk",
      dialogue="dlg_r1_seed_husk", requireFlag=F_HEART, hideFlag="r1_husk_taken")
    return props


def dressing():
    """Second pass: giant glowing fungus, fallen roots, flowstone and small lights so no floor reads empty
    (kept off the paths, the fight floors and the open muster ground by the stair)."""
    # glowing giant mushrooms: tall landmarks in every room
    for x, y, fl in [(18.6, 12.6, False), (41.0, 19.0, True), (24.4, 38.6, False), (70.0, 51.0, True), (77.2, 39.0, False),
                     (107.4, 44.2, True), (100.6, 34.2, False), (55.8, 38.8, True)]:
        P("prop_mushroom_giant", x, y, lit=True, flip=fl)
    # fallen roots and old logs across the floors
    for x, y, fl in [(36.6, 18.6, False), (31.2, 36.2, True), (54.4, 36.2, False), (102.6, 47.0, True), (79.0, 33.6, False),
                     (75.6, 13.8, True), (104.0, 25.2, False)]:
        P("prop_log", x, y, flip=fl, tint="#8a7a92")
    # flowstone clusters along the walls and the front lip
    for x, y in [(45.8, 13.0), (61.2, 19.4), (33.0, 39.2), (25.0, 44.2), (68.6, 44.2), (60.6, 57.6), (35.4, 57.8),
                 (78.6, 28.0), (89.0, 26.8), (101.8, 23.4), (64.8, 26.4), (76.0, 46.0), (86.0, 33.0), (104.6, 37.6),
                 (82.0, 58.6), (93.6, 1.4), (102.6, 1.6), (85.8, 1.8)]:
        P("prop_stalagmite", x, y, flip=(int(x * 10) % 2 == 1))
    for x, y in [(43.8, 34.8), (26.2, 30.0), (64.8, 33.0), (99.6, 25.4), (80.6, 36.8), (106.2, 31.6)]:
        P("prop_rock_large", x, y, flip=(int(y * 10) % 2 == 0))
    # small glowing things (no colliders): fungus, pebbles, bones
    for x, y in [(8.0, 16.0), (16.0, 19.0), (12.0, 27.0), (19.0, 34.0), (6.0, 22.0), (30.0, 25.0), (38.0, 16.0), (44.0, 8.0),
                 (58.0, 20.0), (50.0, 26.5), (29.0, 33.0), (40.0, 41.0), (52.0, 40.0), (64.0, 36.0), (70.0, 41.0),
                 (75.0, 25.0), (93.0, 17.0), (99.0, 15.0), (79.0, 20.0), (82.0, 44.0), (100.0, 39.0), (88.0, 40.0), (106.0, 55.0)]:
        P("prop_mushrooms", x, y, lit=True, color=["#8ff0d2", "#c9a8ff", "#ff9ad0"][int(x + y) % 3])
    for x, y in [(14.0, 22.0), (27.0, 16.5), (47.0, 9.0), (55.0, 21.5), (38.0, 35.0), (66.0, 33.0), (72.0, 15.0), (96.0, 20.0),
                 (84.0, 49.0), (98.6, 50.0), (80.0, 40.0)]:
        P("prop_rock_small", x, y)
    for x, y in [(57.0, 12.5), (74.0, 23.5), (92.0, 11.5), (83.6, 37.0)]:
        P("prop_bones", x, y)
    # creeping blight stains on the floor near the den, the pools' edge and the heart
    for x, y, sc in [(50.0, 20.0, 1.4), (56.0, 9.0, 1.2), (47.0, 46.0, 1.1), (88.0, 44.0, 1.6), (95.0, 46.0, 1.4), (85.0, 10.0, 1.2)]:
        P("decal_blight", x, y, scale=sc)
    # root arches over the side ways
    for x, y in [(26.6, 49.6), (104.6, 51.4), (76.6, 20.4)]:
        P("prop_root_arch", x, y, lit=True)


def paths():
    return [
        {"art": "decal_path_dirt", "points": [[0, 30], [8, 29.4], [16, 25.4], [24, 20.4], [33, 15.6], [42, 14.2], [50, 15]], "width": 2.4},
        {"art": "decal_path_dirt", "points": [[8, 29.4], [10, 35.6], [11.6, 40]], "width": 1.6},
        {"art": "decal_path_dirt", "points": [[11.6, 43], [8.4, 50.4], [5.2, 53.4]], "width": 1.4},
        {"art": "decal_path_dirt", "points": [[47, 19], [47, 26], [47, 31], [48, 37], [49, 43]], "width": 2.2},
        {"art": "decal_path_dirt", "points": [[49, 43], [57, 41.4], [63, 38.6], [67.8, 33.6], [68.2, 27], [70, 21.4], [78, 17], [84, 15.4]], "width": 2.2},
        {"art": "decal_path_dirt", "points": [[89, 17], [94.6, 23.4], [96.4, 30], [95.6, 37], [92.6, 43.4]], "width": 2.2},
    ]


def water():
    return [
        # the Weeping Pools
        {"points": [[28, 46], [30, 50.6], [35, 52], [40, 50.2], [40.6, 45.4], [37, 42.6], [31, 42.8]], "closed": True},
        {"points": [[55.6, 50], [57.6, 54.6], [63, 56], [67.6, 53.6], [67.2, 48.6], [62, 46.6], [57, 47]], "closed": True},
        {"points": [[45.8, 55.2], [47.2, 57.6], [51.4, 57.8], [53, 55.2], [50.6, 53.4], [46.8, 53.6]], "closed": True},
        # Mire Hollow's bogs
        {"points": [[70.6, 5.2], [73.4, 9.6], [79, 10.4], [83, 8], [82.4, 3.6], [76, 2]], "closed": True},
        {"points": [[98.8, 4.2], [100.2, 9.4], [105.4, 11.2], [108.4, 8], [107.4, 3], [103, 2]], "closed": True},
        {"points": [[77.4, 23.6], [78.8, 26.8], [83.4, 27.4], [85, 24.6], [82, 22.4]], "closed": True},
    ]


def enemy(cr, x, y):
    return {"creature": cr, "pos": [x, y]}


def encounters():
    RL, HD, KN = "cr_r1_rootling", "cr_r1_blight_hound", "cr_r1_hollow_knight"
    return [
        {"id": "enc_r1_gallery_rootlings", "pos": [29.4, 20.6], "radius": 4.0,
         "enemies": [enemy(RL, 30.4, 22.2), enemy(RL, 31.6, 20.2), enemy(RL, 29.8, 19.0), enemy(HD, 32.6, 21.8)]},
        {"id": "enc_r1_gallery_hounds", "pos": [36.2, 7.6], "radius": 4.0,
         "enemies": [enemy(HD, 37.0, 8.8), enemy(HD, 38.0, 6.8), enemy(HD, 36.2, 5.8)]},
        {"id": "enc_r1_thornmaw", "pos": [52.0, 15.0], "radius": 6.5, "dialogue": "dlg_r1_thornmaw",
         "enemies": [enemy("cr_r1_thornmaw", 53.4, 16.2)]},
        {"id": "enc_r1_vigil_knights", "pos": [47.4, 37.6], "radius": 4.0,
         "enemies": [enemy(KN, 46.0, 39.4), enemy(KN, 48.8, 39.4), enemy(KN, 47.4, 41.0)]},
        {"id": "enc_r1_twins", "pos": [49.0, 47.6], "radius": 6.0, "dialogue": "dlg_r1_twins",
         "enemies": [enemy("cr_r1_twin_sorrow", 47.2, 49.6), enemy("cr_r1_twin_solace", 50.8, 49.6)]},
        {"id": "enc_r1_pool_wardens", "pos": [62.6, 40.6], "radius": 4.0,
         "enemies": [enemy(KN, 63.4, 42.2), enemy(KN, 61.6, 42.4), enemy(HD, 64.8, 40.8), enemy(HD, 60.4, 40.6)]},
        {"id": "enc_r1_mire_hounds", "pos": [70.4, 19.6], "radius": 4.0,
         "enemies": [enemy(HD, 71.6, 18.2), enemy(HD, 72.4, 20.4), enemy(RL, 70.6, 17.2), enemy(RL, 73.2, 18.8)]},
        {"id": "enc_r1_mother_mire", "pos": [87.0, 14.4], "radius": 6.5, "dialogue": "dlg_r1_mother_mire",
         "enemies": [enemy("cr_r1_mother_mire", 87.0, 15.6), enemy("cr_r1_coven_daughter", 83.6, 14.2), enemy("cr_r1_coven_daughter", 90.4, 14.2)]},
        {"id": "enc_r1_heart_guard", "pos": [96.0, 37.6], "radius": 4.0,
         "enemies": [enemy(KN, 95.0, 39.4), enemy(KN, 97.6, 39.2), enemy(RL, 94.2, 40.6), enemy(RL, 98.6, 40.8)]},
        {"id": "enc_r1_hollow_heart", "pos": [91.0, 46.6], "radius": 7.0, "dialogue": "dlg_r1_hollow_heart",
         "enemies": [enemy("cr_r1_hollow_heart", 91.0, 50.2)]},
    ]


def regions():
    return [
        {"id": "reg_r1_stair", "name": "Lantern Stair", "pos": [10.5, 30], "size": [19, 58], "enterFlag": "reg_r1_stair",
         "text": "The Hollow Heart. Under Mirefen the old spirit grove still breathes, slowly and wrong. Someone has pitched a tent by the stair and is counting things in a ledger."},
        {"id": "reg_r1_gallery", "name": "Root Gallery", "pos": [33, 15], "size": [16, 28],
         "text": "The Root Gallery. Roots as thick as houses hold up the dark. The ones nearest the den have gone grey to the tip."},
        {"id": "reg_r1_den", "name": "Thornmaw's Den", "pos": [52, 13], "size": [18, 22],
         "text": "Thornmaw's Den. Something sleeps here in a nest of thorns and old bones. It sleeps lightly."},
        {"id": "reg_r1_thorn_gate", "pos": [47, 26.6], "size": [9, 4],
         "text": "Black thorns choke the way north. They twitch in time with something huge breathing in the den."},
        {"id": "reg_r1_pools", "name": "Weeping Pools", "pos": [47, 45], "size": [46, 28],
         "text": "The Weeping Pools. Every drop that falls here falls as a tear, and the pools are very full."},
        {"id": "reg_r1_offerings", "pos": [27.6, 55.6], "size": [9, 7], "check": {"skill": "Perception", "dc": 15},
         "checkFlag": "r1_found_offerings",
         "successText": "Behind a veil of thorns in the corner, candle-stubs glint: somebody hid the grove's offerings here, a long time ago.",
         "failText": ""},
        {"id": "reg_r1_mire", "name": "Mire Hollow", "pos": [87, 15], "size": [44, 28],
         "text": "Mire Hollow. The fen has seeped down into the grove, and someone has made a kitchen of it. It smells of nettle soup and bad intentions."},
        {"id": "reg_r1_heart", "name": "Heart Chamber", "pos": [91, 45], "size": [34, 28],
         "text": "The Heart Chamber. The old heart lantern of the grove sits in a cage of roots, and something in it beats. Boom. ...boom."},
    ]


def chests():
    return [
        {"id": "chest_r1_offerings", "pos": [27.2, 58.4], "art": "prop_chest", "lootTable": "lt_r1_chest_offerings", "requireFlag": "r1_found_offerings"},
        {"id": "chest_r1_mires_larder", "pos": [92.4, 7.4], "art": "prop_chest", "lootTable": "lt_r1_chest_larder", "requireFlag": F_MIRE,
         "lockCheck": {"skill": "SleightOfHand", "dc": 15}},
    ]


def npcs():
    return [
        {"npc": "r1_quartermaster", "pos": [11.8, 44.4]},
        {"npc": "r1_hinoki", "pos": [5.0, 54.0]},
        {"npc": "r1_twin_echo", "pos": [49.0, 50.0], "requireFlag": F_TWINS, "hideFlag": "r1_lore_twins"},
        {"npc": "r1_solace_light", "pos": [84.6, 41.4], "requireFlag": "r1_solace_stays"},
    ]


def bundle():
    m = {
        "id": MAP,
        "name": "The Hollow Heart",
        "subtitle": "Raid: a corrupted spirit-grove cavern",
        "width": W, "depth": D,
        "skyTop": "#16141c", "skyBottom": "#2e2a36",
        "ground": "ground_forest", "groundTile": 8,
        "biome": "hollow_heart", "environment": "cave",
        "levelMin": 21, "levelMax": 23,
        "raidSize": 10, "raidReturnMap": "mirefen", "raidReturnSpawn": "from_raid_hollow_heart",
        "fill": 0.5,
        "paths": paths(),
        "water": water(),
        "props": build_props(),
        "npcs": npcs(),
        "encounters": encounters(),
        "chests": chests(),
        "transitions": [
            {"id": "to_mirefen", "pos": [0.7, 30.0], "size": [1.4, 6.0], "targetMap": "mirefen", "targetSpawn": "from_raid_hollow_heart", "label": "Mirefen"}
        ],
        "spawns": [{"id": "default", "pos": [3.4, 30.0]}, {"id": "from_mirefen", "pos": [3.4, 30.0]}],
        "regions": regions(),
        "ambient": {"timeOfDay": "night", "dayNightCycle": False, "ambientColor": "#a890b8", "ambientIntensity": 0.6,
                    "mist": True, "fireflies": True, "drips": True},
        "restArea": False,
        "music": "music_raid",
    }
    return {"_note": "The Hollow Heart raid map (Docs/Expansion.md §1, §8 row r1). Generated by Tools/datagen/r1/gen_r1.py. Owner: r1.",
            "maps": [m]}
