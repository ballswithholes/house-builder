"""npcs.json and companions.json"""
from wn_common import *
import wn_items as I


def V(items, stock=None):
    out = []
    for it in items:
        d = {"item": it}
        if stock and it in stock: d["stock"] = stock[it]
        out.append(d)
    return out


def npc(id, name, title, sprite, dialogue, bark, portrait=None, vendor=None, trains=None, innkeeper=False,
        wanders=False):
    d = {"id": id, "name": name}
    if title: d["title"] = title
    d["sprite"] = sprite
    d["portrait"] = portrait if portrait is not None else ("portrait_" + sprite[4:] if sprite.startswith("npc_") else "")
    if not d["portrait"]: del d["portrait"]
    if dialogue: d["dialogue"] = dialogue
    if vendor: d["vendor"] = vendor
    if trains: d["trains"] = trains
    if innkeeper: d["innkeeper"] = True
    d["bark"] = bark
    if wanders: d["wanders"] = True
    return d


NPCS = [
    npc("elder_maru", "Elder Maru", "Elder of Lanternvale", "npc_elder", "dlg_elder_maru",
        "The lanterns remember everyone who ever lit them. Did you know that?"),
    npc("innkeeper_dorrit", "Dorrit Applewhistle", "Innkeeper of the Sleepy Lantern", "npc_innkeeper",
        "dlg_innkeeper_dorrit", "Boots off the tables, loves!", vendor=V(I.VENDOR_FOOD), innkeeper=True),
    npc("merchant_tilly", "Tilly Brambleback", "General Goods", "npc_merchant", "dlg_merchant_tilly",
        "Potions! Pickles! Lantern oil for the grey!",
        vendor=V(I.VENDOR_GENERAL, {"elixir_wisdom": 3, "elixir_lantern_oil": 5, "potion_minor_rejuvenation": 5,
                                    "reagent_ankh": 5})),
    npc("smith_garrow", "Garrow Ironhand", "Weaponsmith", "npc_smith", "dlg_smith_garrow", "*CLANG.* ...Sorry, what?",
        vendor=V(I.VENDOR_WEAPONS + ["whetstone_garrow"])),
    npc("armorer_bess", "Bess Ironhand", "Armorer", "npc_smith", "dlg_armorer_bess",
        "If it fits, it protects. If it pinches, come back.", vendor=V(I.VENDOR_ARMOR)),
    npc("trainer_odo", "Sir Odo Brightwater", "Warrior & Paladin Trainer", "npc_trainer", "dlg_trainer_odo",
        "Elbows up! Shield higher! Ah — sorry. Habit.", trains=["Warrior", "Paladin"]),
    npc("trainer_fennel", "Fennel Greythorn", "Hunter & Shaman Trainer", "npc_trainer", "dlg_trainer_fennel",
        "Listen. The wind's saying something. Mostly 'it's windy'.", trains=["Hunter", "Shaman"]),
    npc("trainer_quillon", "Magister Quillon Ashby", "Mage & Warlock Trainer", "npc_trainer", "dlg_trainer_quillon",
        "Do not touch the floating books. They bite.", trains=["Mage", "Warlock"]),
    npc("trainer_wick", "Brother Wick", "Priest & Rogue Trainer", "npc_trainer", "dlg_trainer_wick",
        "Every light casts a shadow. I teach both. Tea?", trains=["Priest", "Rogue"]),
    npc("guard_holt", "Sergeant Holt", "Lanternvale Guard", "npc_guard", "dlg_guard_holt",
        "Stay on the path. The path likes you."),
    npc("shepherd_bram", "Bram", "Shepherd", "npc_villager_b", "dlg_shepherd_bram",
        "Thirty-one sheep. Thirty-one. ...Thirty."),
    npc("child_nell", "Nell", "", "npc_child", "dlg_child_nell",
        "Moppet? Moppet! ...He's not lost. He's just hiding very well."),
    npc("child_toby", "Toby", "", "npc_child", "dlg_child_toby", "I'm a knight! This stick is my sword! Its name is Stick!"),
    npc("lamplighter_tobben", "Old Tobben", "Lamplighter", "npc_villager_a", "dlg_lamplighter_tobben",
        "Forty years I've lit these lanterns. Never seen 'em go out on their own."),
    npc("postman_fennick", "Fennick", "Letter-Carrier", "npc_villager_b", "dlg_postman_fennick",
        "Letters! Parcels! ...Mostly apologies for the missing letters!"),
    npc("villager_june", "June Marigold", "Flower-Seller", "npc_villager_a", "dlg_villager_june",
        "Flowers for the lanterns? They like marigolds best.", wanders=True),
    npc("villager_hollis", "Hollis", "Miller", "npc_villager_b", "dlg_villager_hollis",
        "No wind since the great lantern went out. Not a breath."),
    npc("rusk", "Rusk", "Farmhand", "cr_bandit_chief", "dlg_rusk_reformed", "Honest work. Feels strange. Good strange.",
        portrait=""),
    # Whisperwood
    npc("komorebi", "Komorebi", "Spirit of the Wayside Lantern", "npc_spirit", "dlg_komorebi",
        "*A soft chiming, like wind through glass.*"),
    npc("moppet", "Moppet", "Nell's Spirit Friend", "npc_spirit", "dlg_moppet", "*A tiny, worried jingle.*"),
    # Shrine
    npc("warden_spirit", "The Lantern Warden", "Guardian of the Valley", "npc_spirit", "dlg_warden_spirit",
        "*The little stag of light dozes, glowing like a banked hearth.*"),
    # speakers for encounter dialogues (not placed on maps)
    npc("puddlecap", "Chief Puddlecap", "Chief of the Mosslings", "cr_mossling_shaman", "",
        "Puddlecap is chief! Puddlecap has the biggest hat!", portrait=""),
    npc("bandit_lookout", "Bridge Lookout", "Rusk's Crew", "cr_bandit_archer", "", "Oi! Road's tolled!", portrait=""),
    npc("keeper_ishiro", "Keeper Ishiro", "Hollowed Keeper of the Old Shrine", "cr_hollow_spirit", "",
        "...put them out... put them all out...", portrait=""),
    npc("hollow_warden", "The Hollow Warden", "", "cr_hollow_warden", "", "...SO... HUNGRY...", portrait=""),
]


def comp(id, name, cls, title, bio, personality, inspiration, bonus, items, likes, dislikes):
    sb = {"strength": 0, "agility": 0, "stamina": 0, "intellect": 0, "spirit": 0}
    sb.update(bonus)
    return {"id": id, "name": name, "classId": cls, "title": title, "sprite": "comp_" + id, "portrait": "portrait_" + id,
            "bio": bio, "personality": personality, "inspiration": inspiration, "statBonus": sb,
            "startingItems": items, "preferredTalents": [], "recruitDialogue": "dlg_recruit_" + id,
            "likes": likes, "dislikes": dislikes}


G = I.COMPANION_GEAR
COMPANIONS = [
    comp("kael", "Kael", "Warrior", "The Red Wanderer",
         "Ten years ago Kael walked the summoner Isolde up the pilgrim stair as her guardian, and walked back down "
         "alone. He has lived at the Sleepy Lantern ever since — one cup of tea a day, watching the road to "
         "Whisperwood — keeping the promise he made at the top of the mountain: to watch over Isolde's daughter until "
         "she no longer needs watching.",
         "Stoic, terse and dry as old bread, with a buried warmth that surfaces in deeds rather than words. Respects "
         "resolve; has no patience for dithering or cruelty.",
         "Auron-like veteran guardian (FFX): heavy red greatcoat worn off one shoulder over mail, a pale scar closing "
         "one eye, grey-streaked hair, an enormous notched greatsword carried on the shoulder and a battered tea "
         "flask at the hip. Original character.",
         {"strength": 3, "stamina": 3}, G["kael"],
         ["Keeping promises", "Protecting Seren and the defenceless", "Plain speaking", "Facing danger head-on"],
         ["Cruelty", "Lying", "Dithering", "Letting anyone sacrifice themselves"]),
    comp("lys", "Lys", "Mage", "The Belted Sorceress",
         "Lys grew up in Lanternvale, left for the Arcanum of Velmora at fourteen and came back a sorceress with a "
         "reputation and a notebook. Officially she is studying the Hollow. Unofficially, she has never stopped "
         "worrying about the summoner girl she used to walk to the shrine every festival.",
         "Aloof, sardonic and precise; shows affection through criticism. Secretly sentimental — she keeps a book of "
         "pressed marigolds and will deny it to the grave.",
         "Lulu-like black mage (FFX): a long dark dress made of crossing belts, fur-trimmed collar, dark braided hair "
         "pinned with long needles, and a staff topped with a storm-filled orb. Instead of a doll she carries a "
         "pressed-flower book she pretends is research. Original character.",
         {"intellect": 3, "spirit": 1, "stamina": 1}, G["lys"],
         ["Clever deductions", "Respect for magic and scholarship", "Protecting Seren", "Precision"],
         ["Sentimentality (out loud)", "Superstition", "Recklessness", "Mocking scholarship"]),
    comp("seren", "Seren", "Priest", "Summoner on Pilgrimage",
         "Seren is a summoner of the valley, walking the old pilgrimage to rekindle each shrine's lantern before she "
         "climbs to the Heart. Her mother, Isolde, made the same pilgrimage ten years ago and gave the Heart Lantern "
         "everything she had. Seren means to do the same if she must — and is quietly, desperately hoping she won't "
         "have to.",
         "Gentle, earnest and braver than she feels; polite to a fault, stubborn as a mountain once decided. Laughs "
         "when she's nervous.",
         "Yuna-like summoner-pilgrim (FFX): pale blue kimono top over a long flowing hakama skirt, a ringed staff that "
         "chimes with each step, dark hair with a single white streak and a beaded hair ornament. Original character.",
         {"spirit": 3, "intellect": 1, "stamina": 1}, G["seren"],
         ["Mercy", "Kindness to spirits and animals", "Prayer and the old rites", "Finding another way"],
         ["Cruelty", "Greed", "Mocking faith", "Needless violence"]),
    comp("rook", "Rook", "Hunter", "Islander Ringball Captain",
         "Rook captained the Saltreach Gulls to three ringball championships, lost a front tooth in the last final, "
         "and sailed off in search of the best fishing in the world. He found it in Whisperwood's river — just in time "
         "for the Hollow to turn it grey. He travels with Tide, a one-white-eared wolf he raised from a pup.",
         "Big-hearted, loud, superstitious and loyal to the bone; treats every problem like a team sport. Goes quietly "
         "furious when animals suffer.",
         "Wakka-like islander athlete (FFX): sun-browned and broad-shouldered, beaded hair under a bandana, leather "
         "and fur with a sporty number-seven vest, a driftwood recurve bow and a ringball on his hip. Original "
         "character. Suggested hunter pet: a grey wolf named Tide (art key pet_wolf).",
         {"agility": 2, "stamina": 2, "strength": 1}, G["rook"],
         ["Teamwork", "Helping animals", "Courage", "Honest fun"],
         ["Cruelty to beasts", "Cowardice", "Mocking his lucky charms", "Leaving people behind"]),
    comp("pip", "Pip", "Rogue", "Cogwright Scavenger",
         "Pip left the Cogwright caravans — wandering tinkers who rescue junk and turn it into wonders — after "
         "accidentally inventing a self-propelled teapot. She drifts from village to village fixing things that "
         "weren't broken yet. She is terrified of ghosts, which is unfortunate, given the Hollow.",
         "Cheerful, fast-talking, curious and impulsive; generous with everything except shiny things. Hides fear "
         "behind jokes.",
         "Rikku-like goggled scavenger (FFX): oversized three-lens goggles, a bright scarf, short shorts and "
         "mismatched sleeves, a belt of pouches and twin cog-bladed daggers. Original character.",
         {"agility": 3, "intellect": 1, "stamina": 1}, G["pip"],
         ["Treasure and shiny things", "Clever tricks", "Honesty with her", "Kindness"],
         ["Ghosts", "Boring plans", "Bullies", "Wasting good junk"]),
    comp("torvan", "Torvan", "Shaman", "Hornkin Oathkeeper",
         "For a thousand years the Hornkin of the high snows guarded the Warden's herd. Torvan was on watch the night "
         "the grey took the Warden, and did not understand what he heard. He came down the mountain alone to set it "
         "right, and has stood at the foot of the shrine stair ever since, unwilling to fail the Great One twice.",
         "Proud, solemn and sparing with words; formal in speech, fierce in battle and deeply tender with small "
         "creatures. Measures people by deeds, not promises.",
         "Kimahri-like horned beast-folk (FFX): towering, silver-grey fur, great curled ram horns hung with braided "
         "cords and charms, tribal markings and a leaf-bladed spear-staff. Speaks in plain first person. Original "
         "character.",
         {"strength": 2, "stamina": 2, "spirit": 1}, G["torvan"],
         ["Honour", "Strength of will", "Respect for spirits and the mountain", "Keeping vows"],
         ["Boasting", "Cowardice", "Disrespect to the Warden", "Breaking promises"]),
    comp("aldric", "Aldric", "Paladin", "Knight-Errant of Sunmere",
         "The youngest of six sons of the House of Sunmere, Aldric ran away to become a legend after reading a very "
         "cheap chapbook about a knight called Sir Gallant. He has not slain a dragon yet. He has slain several "
         "dandelions, one scarecrow (accidentally) and his own dignity (regularly). He is entirely sincere.",
         "Sunny, brash, impulsive and relentlessly optimistic; talks too much, laughs too loud and is braver than is "
         "strictly sensible. Underneath, desperate to prove he's more than the spare son.",
         "Tidus-like sunny young hero (FFX): tousled blond hair, an asymmetric bright gold tabard with one bare arm, "
         "a sun-painted kite shield and a polished warhammer, and a grin that never quite switches off. Original "
         "character.",
         {"strength": 2, "stamina": 1, "spirit": 1, "intellect": 1}, G["aldric"],
         ["Heroics", "Mercy and second chances", "Encouragement", "Standing up for the weak"],
         ["Cruelty", "Cynicism", "Theft", "Being treated like a child"]),
    comp("morwen", "Morwen", "Warlock", "Scholar of Demons",
         "Morwen Vale-Ashcombe catalogues demons the way others catalogue butterflies: with great care, a pin, and a "
         "slightly alarming fondness. She came to Lanternvale certain the Hollow was a demonic incursion and has "
         "been politely disappointed ever since. Her imp, whom she calls 'Dear', carries her inkpot.",
         "Elegant, courteous, morbidly curious and quietly unsettling; never raises her voice and never loses her "
         "smile. Values knowledge and boldness above comfort.",
         "Original gothic-scholar warlock in FFX-style costume: a high-collared, violet-lined long coat with layered "
         "belts, violet runes stitched along the hems, silver rings, a leather grimoire and a tiny imp on her "
         "shoulder.",
         {"intellect": 2, "stamina": 2, "spirit": 1}, G["morwen"],
         ["Knowledge and curiosity", "Bold choices", "Clever solutions", "Being taken seriously"],
         ["Superstition", "Sentimentality", "Wasted potential", "Being bored"]),
]


def build_npcs():
    return {"_note": "Lanternvale NPCs. The last five entries (puddlecap, bandit_lookout, keeper_ishiro, "
                     "hollow_warden) are dialogue speakers for encounter dialogues and are not placed on maps; "
                     "'rusk' is both the bridge speaker and the reformed farmhand placed in the village.",
            "npcs": NPCS}


def build_companions():
    return {"_note": "Eight recruitable companions, one per class. Any of them can be recruited whatever the "
                     "player's class. Recruit dialogues set <id>_met / <id>_recruited flags; map placements hide on "
                     "<id>_recruited.",
            "companions": COMPANIONS}
