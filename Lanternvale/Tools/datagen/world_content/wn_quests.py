"""quests.json"""
from wn_common import *


def obj(t, target, text, count=1):
    d = {"type": t, "target": target, "text": text}
    if count != 1: d["count"] = count
    return d


def stage(id, desc, objectives, next="", on_complete=None):
    d = {"id": id, "description": desc, "objectives": objectives}
    if next: d["next"] = next
    if on_complete: d["onComplete"] = on_complete
    return d


QUESTS = [
    {
        "id": "mq_lanterns", "name": "The Lanterns Go Dark", "giver": "elder_maru", "level": 8, "main": True,
        "summary": "The spirit-lanterns that keep Lanternvale warm are going dark one by one, and a grey blight called "
                   "the Hollow creeps in behind them. Elder Maru believes the trouble starts at the Old Lantern "
                   "Shrine, high above Whisperwood, where the great stag spirit called the Warden keeps the Heart "
                   "Lantern.",
        "stages": [
            stage("elder", "Speak with Elder Maru beneath Old Kusu, the great camphor tree in the village square.",
                  [obj("Flag", "met_elder", "Speak with Elder Maru")], "wayshrine"),
            stage("wayshrine", "Follow the old pilgrim road east into Whisperwood and find the Wayside Shrine.",
                  [obj("Reach", "reg_wayshrine", "Find the Wayside Shrine")], "komorebi", [XP(200)]),
            stage("komorebi", "Learn what has happened to the Wayside Shrine's lantern from the spirit who keeps it.",
                  [obj("Flag", "met_komorebi", "Speak with the Wayside spirit")], "embers"),
            stage("embers", "Free 3 Spirit Embers from the Hollow wisps drifting through the Grey Grove, east of the "
                            "Old Bridge.",
                  [obj("Collect", "spirit_ember", "Spirit Embers freed", 3)], "embers_return", [XP(400)]),
            stage("embers_return", "Bring the Spirit Embers to Komorebi at the Wayside Shrine.",
                  [obj("Flag", "embers_gathered", "Return to Komorebi")], "rotheart"),
            stage("rotheart", "Burn a way through the blight: defeat Rotheart, the Blighted Treant, whose grey roots "
                              "seal the pilgrim stair at the eastern end of Whisperwood.",
                  [obj("Kill", "cr_hollow_treant", "Rotheart defeated")], "shrine", [XP(600)]),
            stage("shrine", "Climb the pilgrim stair to the Old Lantern Shrine.",
                  [obj("Reach", "reg_shrine_approach", "Reach the Old Lantern Shrine")], "warden", [XP(200)]),
            stage("warden", "Find the source of the Hollow in the shrine's sanctum and face what waits there.",
                  [obj("Kill", "cr_hollow_warden", "The Hollow Warden defeated")], "rekindle"),
            stage("rekindle", "Rekindle the Heart Lantern.",
                  [obj("Flag", "heart_lantern_lit", "Rekindle the Heart Lantern")], "home"),
            stage("home", "Return to Elder Maru in Lanternvale.",
                  [obj("Talk", "elder_maru", "Return to Elder Maru")]),
        ],
        "rewards": {"xp": 1650, "gold": 2500,
                    "choiceItems": ["pilgrim_guardians_blade", "staff_of_the_kindled_hearth", "ember_stitched_jerkin",
                                    "hauberk_of_the_long_night", "kindlewarm_robe", "bow_of_the_lantern_road"]},
    },
    {
        "id": "sq_shepherd", "name": "Wolves at the Fold", "giver": "shepherd_bram", "level": 3,
        "summary": "Wolves have been at Bram's flock every night since the lanterns began to fail. Something in "
                   "Whisperwood is driving them out of the trees.",
        "stages": [
            stage("wolves", "Drive off the wolves troubling Bram's flock in the east pasture of Lanternvale.",
                  [obj("Kill", "cr_wolf", "Grey Wolves driven off", 3)], "report", [XP(150)]),
            stage("report", "Tell Bram the pasture is clear.", [obj("Flag", "shepherd_hunt", "Speak with Bram")],
                  "greymane"),
            stage("greymane", "Track Greymane, the old alpha, to his den beneath the big rocks at the edge of "
                              "Whisperwood, and put him to rest.",
                  [obj("Kill", "cr_greymane", "Greymane put to rest")], "return"),
            stage("return", "Return to Bram in the east pasture.", [obj("Talk", "shepherd_bram", "Return to Bram")]),
        ],
        "rewards": {"xp": 700, "gold": 150,
                    "choiceItems": ["wolfrunner_boots", "shepherds_fleece_vest", "shepherds_crook",
                                    "wolf_tooth_necklace"]},
    },
    {
        "id": "sq_spirit_friend", "name": "Little Lost Light", "giver": "child_nell", "level": 4,
        "summary": "Nell's spirit friend Moppet ran away into Whisperwood when the great lantern went out. Nell is "
                   "not scared about it. Not even a bit. (A bit.)",
        "stages": [
            stage("find", "Find Moppet somewhere quiet in Whisperwood. He likes glowing mushrooms and places where "
                          "grown-ups don't look. Nell's bell might help.",
                  [obj("Flag", "moppet_found", "Find Moppet")], "return"),
            stage("return", "Tell Nell that Moppet is safe.", [obj("Talk", "child_nell", "Return to Nell")]),
        ],
        "rewards": {"xp": 550, "gold": 0, "choiceItems": ["nells_lucky_acorn", "moppets_moss_charm"]},
    },
    {
        "id": "sq_wicks", "name": "Wicks Gone Walkabout", "giver": "lamplighter_tobben", "level": 6,
        "summary": "Old Tobben's spirit-wicks keep disappearing from his stores, leaving behind little muddy "
                   "leaf-shaped footprints. Mosslings!",
        "stages": [
            stage("gather", "Recover 6 Lantern Wicks from the Mosslings of Whisperwood. Their camp, Puddlecap "
                            "Hollow, lies just east of the Wayside Shrine.",
                  [obj("Collect", "lantern_wick", "Lantern Wicks recovered", 6)], "return"),
            stage("return", "Bring the wicks back to Tobben in Lanternvale.",
                  [obj("Flag", "wicks_returned", "Return to Tobben")]),
        ],
        "rewards": {"xp": 800, "gold": 250,
                    "choiceItems": ["lamplighters_gloves", "wayfarers_chain_boots", "mossy_cloak"]},
    },
    {
        "id": "sq_satchel", "name": "Return to Sender", "giver": "postman_fennick", "level": 6,
        "summary": "Fennick the letter-carrier dropped his satchel when a boar charged him on the Whisperwood road, "
                   "and spiders dragged it off into their hollow. Every letter in the valley is in a spider's larder.",
        "stages": [
            stage("find", "Recover Fennick's satchel from the Spiders' Hollow, east of the Old Bridge in Whisperwood.",
                  [obj("Collect", "fennicks_satchel", "Recover Fennick's satchel")], "return"),
            stage("return", "Return the satchel to Fennick in Lanternvale.",
                  [obj("Talk", "postman_fennick", "Return to Fennick")], "deliver"),
            stage("deliver", "Deliver Keeper Ishiro's letter to Elder Maru.",
                  [obj("Flag", "ishiro_letter_read", "Deliver the letter to Elder Maru")]),
        ],
        "rewards": {"xp": 750, "gold": 300,
                    "choiceItems": ["couriers_swift_cloak", "willow_ring", "cutpurses_signet"]},
    },
    {
        "id": "sq_bridge", "name": "Toll at the Old Bridge", "giver": "guard_holt", "level": 9,
        "summary": "Bandits have taken the Old Bridge in Whisperwood and are charging pilgrims a 'toll'. Sergeant "
                   "Holt can't leave the village. He'd like them gone — however you manage it.",
        "stages": [
            stage("bandits", "Deal with Rusk and the bandits holding the Old Bridge in Whisperwood — by blade, by "
                             "word or by coin.",
                  [obj("Flag", "bandits_dealt_with", "Deal with the bandits at the Old Bridge")], "report"),
            stage("report", "Report back to Sergeant Holt in Lanternvale.",
                  [obj("Talk", "guard_holt", "Report to Sergeant Holt")]),
        ],
        "rewards": {"xp": 1000, "gold": 400,
                    "choiceItems": ["bridgewardens_hauberk", "guardsmans_coif", "brightsteel_shortsword",
                                    "mosswood_longbow"]},
    },
]


def build():
    return {"_note": "Main quest (mq_) and side quests (sq_). Reach objectives target region ids; each such region "
                     "also sets an enterFlag with the same name. XP/gold are WoW-like base values.",
            "quests": QUESTS}
