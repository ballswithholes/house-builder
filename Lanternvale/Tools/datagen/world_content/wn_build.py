"""Builds every Lanternvale content bundle into Resources/Data/content/."""
import sys
from wn_common import write
import wn_items, wn_creatures, wn_quests, wn_npcs, wn_dlg_village, wn_dlg_companions, wn_dlg_wilds, wn_maps

CONFIG = {
    "_note": "Game configuration and the specials requested by world content.",
    "config": {
        "startMap": "lanternvale", "startSpawn": "default", "startDialogue": "dlg_opening",
        "xpRate": 4, "maxLevel": 60, "partySize": 4, "baseMoveMetres": 9, "meleeReachMetres": 2.2,
        "startingGold": 1000,
        "startingItems": ["potion_minor_healing", "potion_minor_healing", "potion_minor_healing",
                          "potion_minor_healing", "potion_minor_mana", "potion_minor_mana",
                          "food_rice_ball", "food_rice_ball", "food_rice_ball", "food_rice_ball",
                          "drink_spring_water", "drink_spring_water", "drink_spring_water", "drink_spring_water",
                          "bandage_linen", "bandage_linen"],
    },
    "specials": [
        {"id": "RekindleLanterns", "usedBy": "dlg_warden_spirit (dialogue outcome Special)",
         "behaviour": "Dialogue outcome (no parameters), run when the Heart Lantern is rekindled; the same node also "
                      "sets flags heart_lantern_lit and lanterns_rekindled. Valley-wide relight: on the current map "
                      "call MapView.SetLanternLit(id, true, animate: true) for every lantern object (all spirit-lantern "
                      "props carry a unique interact id: great_lantern, great_lantern_twin, wayside_lantern, "
                      "heart_lantern, lantern_<map>_<n>). Whenever a map is built while flag lanterns_rekindled is "
                      "set, call SetLanternLit(id, true, animate: false) for all of them so the change persists across "
                      "map changes and save/load. Optional polish: a camera flash/bloom pulse and a 'The lanterns of "
                      "Lanternvale are lit!' banner."},
    ],
}

out = {
    "config.json": CONFIG,
    "items.json": wn_items.build(),
    "creatures.json": wn_creatures.build_creatures(),
    "loot.json": wn_creatures.build_loot(),
    "quests.json": wn_quests.build(),
    "npcs.json": wn_npcs.build_npcs(),
    "companions.json": wn_npcs.build_companions(),
    "dialogues_village.json": wn_dlg_village.build(),
    "dialogues_companions.json": wn_dlg_companions.build(),
    "dialogues_whisperwood.json": wn_dlg_wilds.build_ww(),
    "dialogues_shrine.json": wn_dlg_wilds.build_shrine(),
    "map_lanternvale.json": {"maps": [wn_maps.LANTERNVALE]},
    "map_whisperwood.json": {"maps": [wn_maps.WHISPERWOOD]},
    "map_shrine.json": {"maps": [wn_maps.SHRINE]},
}
for name, bundle in out.items():
    p = write(name, bundle)
    print("wrote", p)
