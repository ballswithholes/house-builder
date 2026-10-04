"""dialogues_village.json — opening scene, Elder, vendors, trainers, quest givers and villagers."""
from wn_common import *

MQ = "mq_lanterns"
DLG = []

# =========================================================================== opening
DLG.append(D("dlg_opening", "o1", [
    N("o1", "narrator",
      "Dusk settles over Lanternvale like a warm quilt. Smoke curls from crooked chimneys, a windmill stands idle on "
      "the hill, and beneath the great camphor tree the spirit-lanterns glow the colour of honey.", next="o2"),
    N("o2", "narrator",
      "Then, one by one, the flames under the tree shiver... shrink... and go out. The square falls silent. "
      "Somewhere a child starts to cry, and somewhere else a sheep joins in, out of solidarity.", next="o3"),
    N("o3", "elder_maru",
      "Well. That's the great lantern gone, and the fourth this month. *An old woman with a long pipe peers at you "
      "through the smoke.* You've a traveller's dust on your boots. See anything grey on the road in?",
      [C("Grey? Like ash?", "o4"),
       C("The spirits here are frightened. I can feel it, like a held breath.", "o4_priest", [CLS("Priest")],
         tag="PRIEST"),
       C("That wasn't a fire going out. Something unmade it.", "o4_mage", [CLS("Mage")], tag="MAGE"),
       C("Something drank that light. I'd know the taste anywhere.", "o4_warlock", [CLS("Warlock")], tag="WARLOCK"),
       C("The birds went quiet a full minute before it happened.", "o4_hunter", [CLS("Hunter")], tag="HUNTER"),
       C("The wind just stopped. The spirits of this place are holding very still.", "o4_shaman", [CLS("Shaman")],
         tag="SHAMAN"),
       C("Whatever is doing this, I'll stand against it.", "o4_paladin", [CLS("Paladin")], tag="PALADIN"),
       C("Point me at whatever did it.", "o4_warrior", [CLS("Warrior")], tag="WARRIOR"),
       C("I saw nothing, I was nowhere, and I definitely didn't just pocket that candle stub.", "o4_rogue",
         [CLS("Rogue")], tag="ROGUE")]),
    N("o4", "elder_maru",
      "Like ash that never knew a fire. We call it the Hollow. Where the lanterns go dark it creeps in, and whatever "
      "it touches forgets how to be itself.", next="o5"),
    N("o4_priest", "elder_maru",
      "Then you've the gift, and you're right. The little spirits of this valley sleep in the lantern-light. When it "
      "goes, so do they... and other things wake. We call it the Hollow.", next="o5"),
    N("o4_mage", "elder_maru",
      "Unmade. Yes. That's the word I've been hunting for all month. The thing that does the unmaking, we call the "
      "Hollow.", next="o5"),
    N("o4_warlock", "elder_maru",
      "...Is that so. Then I'll trust your nose, and keep an eye on the rest of you. Whatever it is, we call it the "
      "Hollow.", next="o5"),
    N("o4_hunter", "elder_maru",
      "Aye, they always know first. The wolves have been coming down out of Whisperwood too. Something's emptying "
      "the forest. We call it the Hollow.", next="o5"),
    N("o4_shaman", "elder_maru",
      "Then the spirits are wiser than the rest of us, keeping still. Something is eating this valley's light. We "
      "call it the Hollow.", next="o5"),
    N("o4_paladin", "elder_maru",
      "Bold. I like bold. Bold gets things done, and only occasionally gets eaten. What you'd be standing against, "
      "we call the Hollow.", next="o5"),
    N("o4_warrior", "elder_maru",
      "Ha! If only it had a nose to punch. Perhaps it does. We call it the Hollow, and it's coming down from the "
      "mountain.", next="o5"),
    N("o4_rogue", "elder_maru",
      "Keep the candle, dear. Light's in short supply. What you didn't see, we call the Hollow.", next="o5"),
    N("o5", "elder_maru",
      "Every lantern in this valley is fed by the Heart Lantern at the Old Shrine, up past Whisperwood. If they're "
      "failing, something's wrong up there. I'm too old for the stair, and the village is too frightened to climb it.",
      [C("I'll help.", "o6"),
       C("What's in it for me?", "o5b"),
       C("I'm only passing through.", "o5c")]),
    N("o5b", "elder_maru",
      "A warm bed at the Sleepy Lantern, a hot meal, the gratitude of a whole valley, and my second-best pipe. Also, "
      "if the lanterns fail, the Hollow won't stop at our borders. So: everything, really.",
      [C("Fine. I'll help.", "o6"), C("I'm still only passing through.", "o5c")]),
    N("o5c", "elder_maru",
      "So was everyone who ever lived here, once. Rest at the inn tonight. If you're still passing through in the "
      "morning, I'll not hold it against you. Much.", out=[SET("opening_seen")]),
    N("o6", "elder_maru",
      "Good. Come and find me under Old Kusu once you've your bearings — the big tree, you can't miss it, it's been "
      "there four hundred years. And mind the dark.", out=[SQ(MQ), SET("opening_seen")]),
]))

# ============================================================================ elder
LETTER_CHOICE = C("I have a letter for you, from Keeper Ishiro at the Old Shrine.", "e_letter",
                  [HAS("ishiro_letter")])
ELDER_HUB = [
    LETTER_CHOICE,
    C("Tell me about the Hollow.", "e_lore_hollow"),
    C("Tell me about the Warden.", "e_lore_warden"),
    C("Tell me about Old Kusu.", "e_lore_kusu"),
    C("Goodbye.")]
DLG.append(D("dlg_elder_maru", "e_post", [
    N("e_post", "elder_maru",
      "There you are! Can you hear it? The whole valley's humming. Tobben's weeping into his lamp-oil and pretending "
      "it's smoke. Thank you, truly.",
      [LETTER_CHOICE, C("How is everyone?", "e_post2"),
       C("Keeper Ishiro is at peace now.", "e_ishiro", [F("ishiro_at_peace")], once=True), C("Goodbye.")],
      cond=[QC(MQ)], fallback="e_home"),
    N("e_ishiro", "elder_maru",
      "*She is quiet for a long moment.* Then he's finally on time for something. *She laughs, and wipes her eyes "
      "with the back of her pipe hand.* Thank you for telling me.", [C("Goodbye.")]),
    N("e_post2", "elder_maru",
      "Bram's counting sheep and getting the same number twice running. The children have their spirit-friends back. "
      "And the windmill's turning again — Hollis cried. Don't tell him I told you.", [C("Goodbye.")]),
    N("e_home", "elder_maru",
      "Every lantern in the valley lit itself an hour ago. All at once. The whole village gasped like one big "
      "surprised cat. I take it that was you?",
      [C("The Warden is at peace, and the Heart Lantern burns again.", "e_home2"),
       C("Seren performed the Kindling — with the light of every hearth in the valley, not her life.", "e_home_seren",
         [F("seren_kindled")])],
      cond=[QS(MQ, "home")], fallback="e_brief"),
    N("e_home2", "elder_maru",
      "Then the old stag walks again. Ha! You've given this valley back its light, and its children their "
      "spirit-friends. That's not a small thing. That's the only thing.", out=[CQ(MQ)], next="e_home3"),
    N("e_home_seren", "elder_maru",
      "Not her life. *Her pipe trembles.* Oh, Isolde would have laughed and cried at once. Ten years I've kept that "
      "taper, waiting for someone stubborn enough to try it the other way.",
      out=[CQ(MQ), APP("seren", 10), APP("kael", 10)], next="e_home3"),
    N("e_home3", "elder_maru",
      "Go on, then. The Sleepy Lantern's pouring for free tonight, and you're the reason. Dorrit will pretend it was "
      "her idea.", [C("Goodbye.")]),
    # --- briefing
    N("e_brief", "elder_maru",
      "Ah, our traveller. Sit, sit — Old Kusu doesn't mind. If you're to help us, you'd best hear about the Warden.",
      cond=[QS(MQ, "elder")], fallback="e_progress", next="e_brief2"),
    N("e_brief2", "elder_maru",
      "Every autumn a great stag spirit walks down from the Old Shrine with lanterns hung in his antlers, and relights "
      "every lantern in the valley. We call him the Warden. This year he never came.", next="e_brief3"),
    N("e_brief3", "elder_maru",
      "And every ten years, a summoner climbs to the shrine to kindle the Heart Lantern anew. The last was Isolde. "
      "She lit it... and she never came down.",
      [C("What happened to her?", "e_isolde"),
       CHK("Summoners who 'never come down'... The old Kindling asked for more than prayers, didn't it?", "History",
           12, "e_hist_s", "e_hist_f", once=True),
       CHK("A spirit-flame needs something to burn. What did she give it?", "Religion", 13, "e_rel_s", "e_rel_f",
           once=True),
       C("Where do I start?", "e_brief4")]),
    N("e_isolde", "elder_maru",
      "Nobody truly knows. Her guardian came back down alone and has scarcely said ten words since. He sits outside "
      "the inn most days in a red coat, watching the road.", next="e_brief4"),
    N("e_hist_s", "elder_maru",
      "Sharp. Yes. The old rite asks a summoner to give the Heart a piece of their own light. Most give a little. "
      "Isolde gave everything. That flame has kept us ten years, and now it's spent.",
      out=[SET("knows_kindling_cost"), XP(100)], next="e_brief4"),
    N("e_hist_f", "elder_maru",
      "*She squints at you.* You've read a book about it, haven't you? The wrong book. *She puffs her pipe and says "
      "no more about it.*", next="e_brief4"),
    N("e_rel_s", "elder_maru",
      "Herself. A summoner gives the Heart Lantern a piece of their own spirit. Isolde gave all of hers, so the "
      "valley would have ten bright years. And it did.", out=[SET("knows_kindling_cost"), XP(100)], next="e_brief4"),
    N("e_rel_f", "elder_maru",
      "That's a question for a priest, dear, and you're asking an old woman with a pipe.", next="e_brief4"),
    N("e_brief4", "elder_maru",
      "Take the pilgrim road east into Whisperwood. Halfway up there's a Wayside Shrine with its own little lantern "
      "spirit, Komorebi. If anyone knows what's gone wrong, she does. And take this.",
      out=[GIVE("kindling_taper"), STG(MQ, "wayshrine"), SET("met_elder")], next="e_brief5"),
    N("e_brief5", "elder_maru",
      "The Kindling Taper. Every hearth in Lanternvale gave it a spark the night Isolde climbed. It's never quite gone "
      "out. I've a feeling it wants to go home.",
      [C("I'll keep it safe.", "e_brief_end"), C("Any advice for the road?", "e_advice")]),
    N("e_advice", "elder_maru",
      "Buy potions from Tilly, eat Dorrit's stew, never trust a Mossling with anything shiny — and if you meet a grey "
      "thing that used to be something else, be kind if you can. They were ours, once.", next="e_brief_end"),
    N("e_brief_end", "elder_maru", "Off you go. The lanterns won't wait, and neither will my pipe.",
      [LETTER_CHOICE, C("Goodbye.")]),
    # --- progress
    N("e_progress", "elder_maru",
      "Back already? The Hollow's still creeping, so I assume you're not finished.",
      [C("Remind me where I'm going.", "e_hint_way", [QS(MQ, "wayshrine")]),
       C("The Wayside spirit wants embers from the Hollow wisps.", "e_hint_embers", [QS(MQ, "komorebi")]),
       C("The Wayside spirit wants embers from the Hollow wisps.", "e_hint_embers", [QS(MQ, "embers")]),
       C("The Wayside spirit wants embers from the Hollow wisps.", "e_hint_embers", [QS(MQ, "embers_return")]),
       C("Something called Rotheart seals the shrine stair.", "e_hint_rotheart", [QS(MQ, "rotheart")]),
       C("What will I find at the shrine?", "e_hint_shrine", [QS(MQ, "shrine")]),
       C("What will I find at the shrine?", "e_hint_shrine", [QS(MQ, "warden")]),
       C("The Heart Lantern needs a new flame.", "e_hint_rekindle", [QS(MQ, "rekindle")])] + ELDER_HUB,
      cond=[QA(MQ)], fallback="e_notstarted"),
    N("e_hint_way", "elder_maru",
      "East through Bram's pasture, into Whisperwood, along the pilgrim road. The Wayside Shrine has a red gate, a "
      "little stone fox and a lantern that ought to be lit. You'll know it.", next="e_hub"),
    N("e_hint_embers", "elder_maru",
      "Embers from the wisps? Poor things. They were lantern-flames once, you know. Taking their light back isn't "
      "cruelty. It's a rescue.", next="e_hub"),
    N("e_hint_rotheart", "elder_maru",
      "Rotheart? He guarded the stair when I was a girl — a kind old tree who let children climb him. If he's gone "
      "grey... burn him clean, and don't let it break your heart.", next="e_hub"),
    N("e_hint_shrine", "elder_maru",
      "The sanctum's at the top of the stair. Keeper Ishiro tends it, though he's not written in months, and he used "
      "to write every week. Be careful up there.", next="e_hub"),
    N("e_hint_rekindle", "elder_maru",
      "Then light it. Whatever it costs. *She stops.* No. Not whatever it costs. We've paid that price before. Find "
      "a better way.", next="e_hub"),
    N("e_notstarted", "elder_maru",
      "Still passing through, traveller? The lanterns aren't. They're going out, one by one.",
      [C("I'll help. Tell me what you need.", "e_brief2", out=[SQ(MQ)])] + ELDER_HUB,
      cond=[QNS(MQ)], fallback="e_hub"),
    N("e_hub", "elder_maru", "Anything else, dear? My pipe's going out, and that's one light I can fix myself.",
      ELDER_HUB),
    N("e_lore_hollow", "elder_maru",
      "It isn't a creature. It's an absence — the shape a light leaves behind when it's taken. Beasts it touches go "
      "grey and angry. Spirits it touches forget their own names.", next="e_hub"),
    N("e_lore_warden", "elder_maru",
      "A stag as tall as the inn, made of mist and moss, with a hundred lanterns in his antlers. When I was small I "
      "left honey cakes out for him on Walk Night. He always ate them. Spirits are terribly greedy about honey.",
      next="e_hub"),
    N("e_lore_kusu", "elder_maru",
      "Old Kusu was here before the village. The first lamplighters tied the shimenawa rope round him and asked his "
      "leave to settle. He said yes, or at least he didn't say no, which with trees is much the same.", next="e_hub"),
    N("e_letter", "elder_maru",
      "*She breaks the grey wax and reads aloud, quietly.* 'Maru. The Warden came to the Heart tonight and did not "
      "light his antlers. He drank from it instead. I am afraid. Send someone.' ...Three weeks ago. Oh, Ishiro.",
      out=[TAKE("ishiro_letter"), SET("ishiro_letter_read"), CQ("sq_satchel")], next="e_letter2"),
    N("e_letter2", "elder_maru",
      "If you meet him up there, tell him it arrived. Tell him I'm sorry it was late. He'll understand — he was never "
      "on time for anything in his life.", [C("I'll tell him.", "e_hub")]),
]))

# =========================================================================== innkeeper
DORRIT_HUB = [
    C("I'd like a room for the night.", "i_rest"),
    C("What's cooking?", out=[VENDOR("innkeeper_dorrit")]),
    C("Heard any news?", "i_news", [NF("lanterns_rekindled")]),
    C("Who's the man in the red coat outside?", "i_kael", [NF("recruited_kael"), NF("lanterns_rekindled")]),
    CHK("Any chance of a discount for a hero-in-training?", "Persuasion", 10, "i_disc_s", "i_disc_f", once=True),
    C("Goodbye.")]
DLG.append(D("dlg_innkeeper_dorrit", "i_post", [
    N("i_post", "innkeeper_dorrit",
      "The hero of the hour! Sit down, sit down — tonight it's on the house, and I'll hear no argument. There'll be "
      "singing later, I'm afraid. Hollis has been practising.", DORRIT_HUB, cond=[F("lanterns_rekindled")],
      fallback="i_greet"),
    N("i_greet", "innkeeper_dorrit",
      "Welcome to the Sleepy Lantern! Mind the cat, mind the step, mind your head on the beam. Hungry? Tired? Both? "
      "It's usually both.", DORRIT_HUB),
    N("i_hub", "innkeeper_dorrit", "Anything else, love?", DORRIT_HUB),
    N("i_rest", "innkeeper_dorrit",
      "Top of the stairs, second door. The beds are feather, the quilts are patchwork, and the floorboard by the "
      "window sings. Sweet dreams.", out=[REST()]),
    N("i_news", "innkeeper_dorrit",
      "Bram's lost three ewes to wolves this week. Old Tobben swears Mosslings are nicking his wicks. And poor Fennick "
      "came back from Whisperwood without his mailbag, white as milk. Something about spiders.", next="i_news2"),
    N("i_news2", "innkeeper_dorrit",
      "Oh, and Sergeant Holt's in a state about bandits at the Old Bridge. And little Nell's lost her spirit-friend. "
      "Busy month for bad news, and it's not even harvest.", next="i_hub"),
    N("i_kael", "innkeeper_dorrit",
      "Kael? Pays for a week at a time, drinks one cup of tea a day, and watches the road. Ten years now. He's not "
      "unkind, love. He's just... waiting.", next="i_hub"),
    N("i_disc_s", "innkeeper_dorrit",
      "Ha! Cheeky. I like cheeky. Here — two bowls of stew on the house, and don't tell the cat, he'll want one.",
      out=[GIVE("food_lantern_stew", 2)], next="i_hub"),
    N("i_disc_f", "innkeeper_dorrit",
      "Nice try, love. Prices are prices. The cat, however, accepts compliments free of charge.", next="i_hub"),
]))

# =========================================================================== merchant
TILLY_HUB = [
    C("Show me your wares.", out=[VENDOR("merchant_tilly")]),
    C("Lantern oil? Against the Hollow?", "t_oil"),
    CHK("Your scales are off. Only slightly — and in your favour.", "Investigation", 12, "t_inv_s", "t_inv_f",
        once=True),
    C("Goodbye.")]
DLG.append(D("dlg_merchant_tilly", "t_post", [
    N("t_post", "merchant_tilly",
      "Business is booming! Everyone wants lantern oil for the festival. I'm calling it 'Festival Oil' now. It's the "
      "same oil. Don't tell anyone.", TILLY_HUB, cond=[F("lanterns_rekindled")], fallback="t_greet"),
    N("t_greet", "merchant_tilly",
      "Tilly Brambleback's General Goods! Potions, pickles, lantern oil and wands. The wands are new. The pickles "
      "are not.", TILLY_HUB),
    N("t_hub", "merchant_tilly", "What else can I tempt you with?", TILLY_HUB),
    N("t_oil", "merchant_tilly",
      "My Lantern-Oil Tonic! Lamp oil, honey, camphor and a great deal of hope. Keeps the grey off you — I've seen it "
      "work. Mostly I've seen people feel better, which counts.", next="t_hub"),
    N("t_inv_s", "merchant_tilly",
      "...Oh, stars. Twenty years I've had those scales. *She presses a few bottles into your hands.* Take these, and "
      "please don't mention it to Sergeant Holt.", out=[GIVE("potion_minor_healing", 3), XP(50)], next="t_hub"),
    N("t_inv_f", "merchant_tilly",
      "*She follows your gaze to the scales.* Those are antiques, dear. Antiques are allowed to be eccentric.",
      next="t_hub"),
]))

# ========================================================================= weaponsmith
GARROW_HUB = [
    C("Let me see your weapons.", out=[VENDOR("smith_garrow")]),
    CHK("Need a hand with that anvil?", "Athletics", 13, "g_ath_s", "g_ath_f", once=True),
    C("You and the armorer look alike.", "g_bess"),
    C("You're working late.", "g_late", [NF("lanterns_rekindled")]),
    C("Goodbye.")]
DLG.append(D("dlg_smith_garrow", "g_greet", [
    N("g_greet", "smith_garrow",
      "*CLANG.* Garrow Ironhand, weaponsmith. Swords, axes, maces, bows — if it's pointy, heavy, or pointy AND heavy, "
      "I've got it.", GARROW_HUB),
    N("g_hub", "smith_garrow", "*He wipes his hands on his apron.* What else?", GARROW_HUB),
    N("g_ath_s", "smith_garrow",
      "Hah! You lifted the whole thing! Bess owes me a silver. Here — take some whetstones. They like a strong arm.",
      out=[GIVE("whetstone_garrow", 3), XP(50)], next="g_hub"),
    N("g_ath_f", "smith_garrow",
      "*You heave. The anvil does not.* It likes you, it's just not ready to move in together. Takes a while, with "
      "anvils.", next="g_hub"),
    N("g_bess", "smith_garrow",
      "Bess? My sister. She does armour, I do weapons, we both do arguing. You'll need both from us, so we've agreed "
      "to be civil in front of customers.", next="g_hub"),
    N("g_late", "smith_garrow",
      "Can't sleep. Every time a lantern goes out I hear it, like a bell that stops ringing. So I hit metal until "
      "I can't hear it any more.", next="g_hub"),
]))

# ============================================================================ armorer
BESS_HUB = [
    C("Show me your armour.", out=[VENDOR("armorer_bess")]),
    C("Why couldn't I wear the plate?", "b_plate"),
    CHK("You're worried about your brother.", "Insight", 11, "b_ins_s", "b_ins_f", once=True),
    C("Goodbye.")]
DLG.append(D("dlg_armorer_bess", "b_greet", [
    N("b_greet", "armorer_bess",
      "Bess Ironhand, armorer. Cloth, leather, mail and plate — and before you ask, yes, the plate's real, and no, "
      "you probably can't wear it yet.", BESS_HUB),
    N("b_hub", "armorer_bess", "Anything else? I've got buckles to polish.", BESS_HUB),
    N("b_plate", "armorer_bess",
      "Plate's a craft, not a coat. Takes years of drill to move in it without falling over. Come back when you've "
      "the scars for it — or buy it now and hang it on the wall. I'm not fussy.", next="b_hub"),
    N("b_ins_s", "armorer_bess",
      "...He's been forging all night since the lanterns started going out. Says it keeps his hands busy. Tell him to "
      "sleep, if he'll listen to anyone.", out=[XP(50), SET("bess_worried")], next="b_hub"),
    N("b_ins_f", "armorer_bess", "I'm worried about my prices, she says briskly, and nothing more.", next="b_hub"),
]))


# ============================================================================ trainers
def trainer(dlg, npc, greet, cls_a, line_a, reply_a, cls_b, line_b, reply_b, check, post):
    s_id, f_id = "tr_chk_s", "tr_chk_f"
    hub = [
        C(line_a, "tr_a", [CLS(cls_a)], tag=cls_a.upper()),
        C(line_b, "tr_b", [CLS(cls_b)], tag=cls_b.upper()),
        C("I'd like to train.", out=[TRAIN(npc)]),
        C("I want to unlearn my talents.", "tr_respec"),
        CHK(check[0], check[1], check[2], s_id, f_id, once=True),
    ]
    extra = []
    if npc == "trainer_odo":
        hub.append(C("Could I practise on that training dummy?", "tr_dummy"))
        extra.append(N("tr_dummy", npc,
                       "Sir Turnip? By all means. He's never once hit back, which is more than I can say for my "
                       "students. Mind his nose.", out=[FIGHT("enc_training_dummy")]))
    hub.append(C("Goodbye."))
    return D(dlg, "tr_post", [
        N("tr_post", npc, post, hub, cond=[F("lanterns_rekindled")], fallback="tr_greet"),
        N("tr_greet", npc, greet, hub),
        N("tr_hub", npc, "Anything else?", hub),
        N("tr_a", npc, reply_a, out=[TRAIN(npc)]),
        N("tr_b", npc, reply_b, out=[TRAIN(npc)]),
        N("tr_respec", npc,
          "Unlearning is harder than learning, and costs more besides. Shall we begin?",
          [C("Yes, wipe the slate.", out=[RESPEC(npc)]), C("On second thought, no.", "tr_hub")]),
        N(s_id, npc, check[3], out=check[5], next="tr_hub"),
        N(f_id, npc, check[4], next="tr_hub"),
    ] + extra)


DLG.append(trainer(
    "dlg_trainer_odo", "trainer_odo",
    "Sir Odo Brightwater — retired, unretired, and retired again. I train warriors and paladins: the shouty sort and "
    "the shiny sort.",
    "Warrior", "Teach me something that hits harder.",
    "Harder! Splendid. Stance, breath, follow-through. The shouting comes naturally.",
    "Paladin", "I seek the Light's instruction.",
    "Then let's see how brightly you burn. Seals first, judgement after, humility always.",
    ("You hold your sword like a man who lost someone to it.", "Insight", 12,
     "*Odo's smile fades.* My squire. Forty years ago, on the pilgrim stair. *He straightens.* Which is why I teach "
     "you lot to come home. Again.",
     "Odo laughs. 'I hold it like a man with a bad shoulder, friend.'", [XP(75)]),
    "Ha! The lanterns are lit and my knees have stopped aching. Coincidence? I think not. What'll it be?"))
DLG.append(trainer(
    "dlg_trainer_fennel", "trainer_fennel",
    "Fennel Greythorn. I teach hunters to listen to the wild, and shamans to listen to the spirits under it. Most "
    "folk can't tell the difference. The wolves can.",
    "Hunter", "I need to sharpen my aim.",
    "Breathe out, loose on the empty. And feed your pet first. Always first.",
    "Shaman", "The elements are restless.",
    "They are. The Hollow's made them jumpy. Let me show you how to calm them — and how to point them.",
    ("The pasture grass is greying at the tips. Is the Hollow in the soil?", "Nature", 12,
     "...Good eye. Not in the soil — in the light. Plants are just the first to notice. Take this; it's more use to "
     "you than me.",
     "'Could be drought,' Fennel says, very unconvinced.", [GIVE("elixir_lantern_oil"), XP(75)]),
    "Hear that? Birdsong. Proper, silly, morning birdsong. I'd forgotten how loud it is. Training?"))
DLG.append(trainer(
    "dlg_trainer_quillon", "trainer_quillon",
    "Magister Quillon Ashby. Do not touch the floating books, do not feed the floating books, and do not, under any "
    "circumstances, read the floating books aloud.",
    "Mage", "I'm ready for more advanced spells.",
    "Ah, a scholar! Fireball? Frostbolt? Something tasteful in arcane? Sit. Don't touch anything.",
    "Warlock", "I require... specialised instruction.",
    "Of course you do. I teach it under protest, with the windows open.",
    ("The lantern-flames run on spirit, not oil. That's why the Hollow can eat them.", "Arcana", 14,
     "Exactly! At last, someone who reads! Here — some of my study scrolls. Don't let them get damp. They sulk.",
     "'Close,' he says, in the tone of a man who means 'not remotely'.", [GIVE("scroll_intellect", 2), XP(100)]),
    "The floating books have started humming. I think they're happy. Or hungry. What do you need?"))
DLG.append(trainer(
    "dlg_trainer_wick", "trainer_wick",
    "Brother Wick. I tend the village shrine, and in the evenings I teach... other skills. I was a cutpurse once, "
    "before the lanterns caught me. Light and shadow, friend. You can't have one without the other.",
    "Priest", "I would learn more of the Light.",
    "Then pray with your hands as well as your heart. Healing is work, not wishing.",
    "Rogue", "I'm after something quieter.",
    "Quieter, quicker, kinder when it counts. Let's begin.",
    ("(Pick his pocket while he's talking.)", "SleightOfHand", 13,
     "You lift a little purse. He doesn't notice — then grins and pats the empty pocket. 'Good! Keep it. Consider "
     "it tuition, refunded.'",
     "*His hand closes, very gently, over yours.* 'Ah. Not yet. But your heart was in it.'", [GG(150), XP(50)]),
    "The shrine bell rang on its own last night. Nobody touched it. I choose to believe it was saying thank you."))

# ============================================================================ guard
DLG.append(D("dlg_guard_holt", "h_done", [
    N("h_done", "guard_holt",
      "Bridge is quiet, traders are moving and nobody's been stabbed all week. I could get used to this.",
      [C("And Rusk's people?", "h_done_rusk", [F("bandits_peaceful")]), C("Goodbye.")],
      cond=[QC("sq_bridge")], fallback="h_report"),
    N("h_done_rusk", "guard_holt",
      "Mending fences in the east field, singing badly. Strangest thing I've seen in twenty years of guarding, and I "
      "once saw a goose run for council.", [C("Goodbye.")]),
    N("h_report", "guard_holt", "Well? Is the bridge clear?",
      [C("They were farmers. Their fields went grey, so they took the bridge. I sent them here for honest work.",
         "h_rep_peace", [F("bandits_peaceful")]),
       C("They won't be back. I made sure of it.", "h_rep_scared", [F("bandits_scared")]),
       C("I paid them to leave the valley.", "h_rep_paid", [F("bandits_paid")]),
       C("The bandits won't trouble anyone again.", "h_rep_fight",
         [NF("bandits_peaceful"), NF("bandits_scared"), NF("bandits_paid")])],
      cond=[QS("sq_bridge", "report")], fallback="h_active"),
    N("h_rep_peace", "guard_holt",
      "...Farmers. Of course they were. Right. Dorrit needs hands and the mill needs a strong back. I'll find them "
      "work. You did a kinder thing than I asked, friend.",
      out=[CQ("sq_bridge"), APP("seren", 5), APP("aldric", 5)], next="h_end"),
    N("h_rep_scared", "guard_holt",
      "Good. Can't say I'll miss them. Can't say I'd want to live in the next valley over, either.",
      out=[CQ("sq_bridge")], next="h_end"),
    N("h_rep_paid", "guard_holt",
      "You... paid them. With money. *He rubs his face.* Well, it worked, and nobody's bleeding. I'll put it in the "
      "report as 'diplomacy'.", out=[CQ("sq_bridge"), APP("pip", -3), APP("seren", 3)], next="h_end"),
    N("h_rep_fight", "guard_holt",
      "Hm. Not how I'd have liked it, but it's done. The road's yours to walk again — yours, and every pilgrim's.",
      out=[CQ("sq_bridge")], next="h_end"),
    N("h_end", "guard_holt", "Here. The valley's thanks, such as the guard budget allows. Which is not very much.",
      [C("Goodbye.")]),
    N("h_active", "guard_holt",
      "The Old Bridge, friend. Past the Wayside Shrine. Bandits, tolls, unhappy pilgrims. You know the one.",
      [C("Goodbye.")], cond=[QS("sq_bridge", "bandits")], fallback="h_greet"),
    N("h_greet", "guard_holt",
      "Sergeant Holt, Lanternvale Guard. All of it — I'm the whole guard. What can I do for you?",
      [C("Any trouble around?", "h_offer", [QNS("sq_bridge"), NF("bandits_dealt_with")]),
       C("About the bandits at the Old Bridge — it's already handled.", "h_report",
         [QNS("sq_bridge"), F("bandits_dealt_with")], out=[SQ("sq_bridge"), STG("sq_bridge", "report")]),
       CHK("You're more worried than you're letting on.", "Insight", 11, "h_ins_s", "h_ins_f", once=True),
       C("Goodbye.")]),
    N("h_offer", "guard_holt",
      "Bandits at the Old Bridge in Whisperwood, charging 'tolls' to anyone on the pilgrim road. Usually that means a "
      "stabbing, eventually. I can't leave the village. You could.",
      [C("I'll deal with them.", "h_accept", out=[SQ("sq_bridge")]), C("Not right now.")]),
    N("h_accept", "guard_holt",
      "Run them off however you like. I'd prefer the kind of 'however' that doesn't end with you dead. Good luck.",
      [C("Goodbye.")]),
    N("h_ins_s", "guard_holt",
      "*He lowers his voice.* Bandits I can handle. It's the grey that frightens me. You can't arrest a colour.",
      out=[XP(25)], next="h_greet"),
    N("h_ins_f", "guard_holt", "Worried? I'm a guard. Worry's the uniform.", next="h_greet"),
]))

# ============================================================================ shepherd
SQS = "sq_shepherd"
DLG.append(D("dlg_shepherd_bram", "b_done", [
    N("b_done", "shepherd_bram",
      "The flock's sleeping easy, and so am I, first time in weeks. If you ever want a lamb named after you, just say "
      "the word.", [C("Goodbye.")], cond=[QC(SQS)], fallback="b_return"),
    N("b_return", "shepherd_bram", "Is it done? Is he...?",
      [C("Greymane won't trouble your flock again.", "b_ret2")],
      cond=[QS(SQS, "return")], fallback="b_greymane"),
    N("b_ret2", "shepherd_bram",
      "Poor old devil. He kept this valley's wolves honest for ten years, you know. It's not his fault the grey got "
      "him. *He sniffs.* Thank you. Truly.", out=[CQ(SQS)], next="b_done_end"),
    N("b_done_end", "shepherd_bram", "Take something for your trouble. Not a sheep. Something else.",
      [C("Goodbye.")]),
    N("b_greymane", "shepherd_bram",
      "His den's just inside Whisperwood, under the big rocks back from the road. You'll smell it before you see it. "
      "Mind yourself — he's not called the old alpha for nothing.", [C("Goodbye.")],
      cond=[QS(SQS, "greymane")], fallback="b_report"),
    N("b_report", "shepherd_bram",
      "You chased 'em off? All three? Bless you. But they'll be back. They're not hungry, see — they're scared. "
      "Running from something in the woods.",
      [CHK("Let me look at those tracks again.", "Survival", 11, "b_surv_s", "b_surv_f", once=True),
       C("Wolves only run from bigger wolves.", "b_hunt", [CLS("Hunter")], out=[XP(50)], tag="HUNTER"),
       C("What are they running from?", "b_hunt")],
      cond=[QS(SQS, "report")], fallback="b_active"),
    N("b_surv_s", "shepherd_bram",
      "*You kneel. The prints are panicked, wide-spaced — and one set is huge, with a grey crust in each print like "
      "frost that never melts.* 'Greymane,' Bram whispers. 'The old alpha.' He presses a potion into your hand.",
      out=[XP(100), GIVE("potion_lesser_healing")], next="b_hunt"),
    N("b_surv_f", "shepherd_bram", "Mud, mostly. And sheep. Bram looks hopeful, then less hopeful.", next="b_hunt"),
    N("b_hunt", "shepherd_bram",
      "There's an old alpha, Greymane, dens at the edge of Whisperwood. If he's gone grey like the rest, the pack'll "
      "follow him anywhere. Would you...? He was a good wolf, once. Put him to rest.",
      [C("I'll find him.", out=[STG(SQS, "greymane"), SET("shepherd_hunt")])]),
    N("b_active", "shepherd_bram",
      "They come at dusk into the far corner of the east pasture. Three of 'em, I think — though they look like "
      "thirty when you're one man with a crook.", [C("Goodbye.")], cond=[QS(SQS, "wolves")], fallback="b_greet"),
    N("b_greet", "shepherd_bram", "Oh! Hello. Sorry. Counting. Thirty-one sheep. Thirty. ...Thirty.",
      [C("Is something wrong?", "b_offer"),
       CHK("That ewe's limping. Let me take a look.", "Nature", 9, "b_nat_s", "b_nat_f", once=True),
       C("Goodbye.")]),
    N("b_offer", "shepherd_bram",
      "Wolves. They've been at the flock every night since the lanterns started failing. Lost three ewes already. "
      "I'm a shepherd, not a fighter. Would you see them off?",
      [C("I'll handle it.", "b_accept", out=[SQ(SQS), SET("shepherd_quest")]), C("Sorry, not now.")]),
    N("b_accept", "shepherd_bram",
      "Bless you. They gather in the far corner of the pasture, by the hay. Mind the gate — Daisy bites.",
      [C("Goodbye.")]),
    N("b_nat_s", "shepherd_bram",
      "*You tease a thorn out of her hoof. She headbutts you, affectionately. Bram beams like a proud father.*",
      out=[XP(50)], next="b_greet"),
    N("b_nat_f", "shepherd_bram", "*She headbutts you. Not affectionately.*", next="b_greet"),
]))

# ============================================================================ Nell
SQN = "sq_spirit_friend"
DLG.append(D("dlg_child_nell", "n_done", [
    N("n_done", "child_nell",
      "Moppet says thank you! He says it in jingles. That one meant 'thank you'. That one meant 'cake'.",
      [C("Goodbye.")], cond=[QC(SQN)], fallback="n_return"),
    N("n_return", "child_nell", "Did you— is he— MOPPET!",
      [C("He was hiding under the glowing mushrooms. He's safe.", "n_ret2")],
      cond=[QS(SQN, "return")], fallback="n_active"),
    N("n_ret2", "child_nell",
      "*A tiny spirit tumbles into Nell's arms with a jingle.* He was scared of the dark, he says. I told him I'm not "
      "scared of anything. ...I was a bit scared. This is my luckiest thing. You have it. You need it more.",
      out=[CQ(SQN)], next="n_ret3"),
    N("n_ret3", "child_nell", "And if you ever get scared, ring a bell. Somebody always comes.", [C("Goodbye.")]),
    N("n_active", "child_nell",
      "Ring my bell where it's quiet, and he'll come. He likes mushrooms that glow and places where grown-ups never "
      "look. And honey cake. He REALLY likes honey cake.", [C("Goodbye.")], cond=[QS(SQN, "find")],
      fallback="n_greet"),
    N("n_greet", "child_nell",
      "Have you seen Moppet? He's small and green-ish and he's got a leaf on his head and he jingles. He ran away when "
      "the big lantern went out.",
      [C("Who's Moppet?", "n_who"),
       CHK("You're scared too, aren't you?", "Insight", 8, "n_ins_s", "n_ins_f", once=True),
       C("I'll look for him.", "n_offer"),
       C("Goodbye.")]),
    N("n_who", "child_nell",
      "My spirit friend! Nearly every kid in Lanternvale has one. They live in the lantern-light. When it went dark he "
      "got scared and ran to the woods. I'm not allowed in the woods.", next="n_offer"),
    N("n_ins_s", "child_nell", "...A bit. Don't tell Toby.", out=[XP(25)], next="n_offer"),
    N("n_ins_f", "child_nell", "I'm not scared of ANYTHING, says Nell, very loudly.", next="n_offer"),
    N("n_offer", "child_nell",
      "Take my bell. Ring it where it's quiet, and he'll know you're my friend. Please find him?",
      [C("I'll bring him home.", "n_accept", out=[SQ(SQN), GIVE("nells_bell")]), C("Maybe later.")]),
    N("n_accept", "child_nell",
      "Thank you thank you THANK you. He likes honey cake, if you need to bribe him. I always need to bribe him.",
      [C("Goodbye.")]),
]))

# ============================================================================ Tobben
SQW = "sq_wicks"
DLG.append(D("dlg_lamplighter_tobben", "t_done_friend", [
    N("t_done_friend", "lamplighter_tobben",
      "I hung a lantern at the edge of Whisperwood, for the little thieves. Every morning there's a mushroom left on "
      "the post. Every morning I eat it. We've an understanding.", [C("Goodbye.")],
      cond=[QC(SQW), F("mosslings_befriended")], fallback="t_done"),
    N("t_done", "lamplighter_tobben",
      "Every wick back in its lantern. Not that it matters while the Heart's dark... but it will matter, once it's "
      "lit. It will.", [C("Goodbye.")], cond=[QC(SQW)], fallback="t_return"),
    N("t_return", "lamplighter_tobben", "Did you find them? My wicks?",
      [C("The Mosslings gave them back. They only took them to light a lantern of their own — they're scared of the "
         "Hollow too.", "t_ret_friend", [HAS("lantern_wick", 6), F("mosslings_befriended")]),
       C("Here. Six wicks, slightly nibbled.", "t_ret", [HAS("lantern_wick", 6), NF("mosslings_befriended")]),
       C("Not yet.", cond=[cond("NotHasItem", "lantern_wick", amount=6)])],
      cond=[QS(SQW, "return")], fallback="t_active"),
    N("t_ret_friend", "lamplighter_tobben",
      "...A lantern of their own. Forty years I've shooed them off my stores, and they only wanted the same as the "
      "rest of us. I'll hang one for them at the forest edge. Light's for everyone.",
      out=[TAKE("lantern_wick", 6), SET("wicks_returned"), CQ(SQW), APP("seren", 5), APP("rook", 3)], next="t_thanks"),
    N("t_ret", "lamplighter_tobben",
      "Slightly nibbled! They nibble everything. Thank you — I'll have these trimmed and back in their lanterns by "
      "nightfall.", out=[TAKE("lantern_wick", 6), SET("wicks_returned"), CQ(SQW)], next="t_thanks"),
    N("t_thanks", "lamplighter_tobben", "Here. Lamplighter's wages, and a lamplighter's thanks.", [C("Goodbye.")]),
    N("t_active", "lamplighter_tobben",
      "Little muddy leaf-shaped footprints, all over my stores. Mosslings! They camp just east of the Wayside Shrine. "
      "Six wicks, that's all I need. Don't let them talk you into a trade. They always win the trade.",
      [C("Goodbye.")], cond=[QS(SQW, "gather")], fallback="t_greet"),
    N("t_greet", "lamplighter_tobben",
      "Ah! Mind the ladder. Forty years I've lit these lanterns, and I've never seen them go out on their own. Now "
      "they won't relight. And someone's been stealing my wicks besides.",
      [C("Stealing wicks?", "t_offer"),
       CHK("Why won't they relight?", "Arcana", 11, "t_arc_s", "t_arc_f", once=True),
       C("Goodbye.")]),
    N("t_offer", "lamplighter_tobben",
      "Spirit-wicks — braided under the full moon, blessed at the shrine. The Mosslings have been pinching them. "
      "Fetch me back six and I'll make it worth your while.",
      [C("I'll get your wicks back.", "t_accept", out=[SQ(SQW)]), C("Not right now.")]),
    N("t_accept", "lamplighter_tobben",
      "Bless you. Their camp's in Whisperwood, just past the Wayside Shrine. Look for tiny tents and a lot of "
      "very guilty faces.", [C("Goodbye.")]),
    N("t_arc_s", "lamplighter_tobben",
      "*You realise:* the flame isn't fire, it's spirit — it has to come down from the Heart Lantern. Tobben stares. "
      "'Forty years, and nobody ever explained it that neatly.'", out=[XP(50)], next="t_greet"),
    N("t_arc_f", "lamplighter_tobben",
      "'Damp?' you suggest. Tobben gives you a look that has outlasted forty winters.", next="t_greet"),
]))

# ============================================================================ Fennick
SQP = "sq_satchel"
DLG.append(D("dlg_postman_fennick", "f_done", [
    N("f_done", "postman_fennick",
      "Every letter delivered! Well. Nearly every letter. One was addressed to 'the moon'. I'm still working on that "
      "one.", [C("Goodbye.")], cond=[QC(SQP)], fallback="f_deliver"),
    N("f_deliver", "postman_fennick",
      "Did the Elder get Ishiro's letter? I keep thinking... if I'd not dropped it...",
      [C("I'm taking it to her now.", cond=[HAS("ishiro_letter")]), C("Goodbye.")],
      cond=[QS(SQP, "deliver")], fallback="f_early"),
    N("f_early", "postman_fennick", "That's— that's MY SATCHEL!",
      out=[SQ(SQP), STG(SQP, "return")], next="f_return",
      cond=[QNS(SQP), HAS("fennicks_satchel")], fallback="f_return"),
    N("f_return", "postman_fennick",
      "My satchel! You found my satchel! Oh, it's sticky. Why is it sticky? No — don't tell me.",
      [C("Here you go.", "f_ret2"),
       CHK("(Slip a letter out to read before you hand it over.)", "SleightOfHand", 12, "f_peek_s", "f_peek_f",
           once=True)],
      cond=[HAS("fennicks_satchel")], fallback="f_active"),
    N("f_peek_s", "narrator",
      "You palm a letter sealed with grey wax: 'To Elder Maru, from the Old Shrine. URGENT.' The handwriting shakes. "
      "You slide it back before Fennick notices.", out=[SET("peeked_letter"), APP("aldric", -3), APP("pip", 3)],
      next="f_ret2"),
    N("f_peek_f", "postman_fennick",
      "Ah-ah! *He catches your wrist.* Lanternvale Post. Sacred trust. *He looks more hurt than angry.*",
      out=[APP("aldric", -2)], next="f_ret2"),
    N("f_ret2", "postman_fennick",
      "*He sorts the letters with shaking hands.* Seed catalogue, seed catalogue, love letter — not mine, sadly — "
      "and... oh no. This one's from the Old Shrine. Keeper Ishiro, to the Elder. It's three weeks late.",
      out=[TAKE("fennicks_satchel")], next="f_ret3"),
    N("f_ret3", "postman_fennick",
      "I can't bring it to her. Three weeks late, from the shrine, with the lanterns failing? She'll look at me and "
      "I'll know. ...Would you take it? Please?",
      [C("I'll take it to her.", "f_take"),
       CHK("It isn't your fault, Fennick. A spider took it, not you.", "Insight", 12, "f_ins_s", "f_ins_f",
           once=True)]),
    N("f_ins_s", "postman_fennick",
      "*He laughs, a bit wetly.* Spiders. Of all the things. *He straightens his cap.* You're right. I'll come by "
      "later and tell her myself. But you go first. Please.", out=[APP("seren", 3), XP(50)], next="f_take"),
    N("f_ins_f", "postman_fennick", "*He only shakes his head and presses the letter into your hands.*",
      next="f_take"),
    N("f_take", "postman_fennick",
      "Thank you. Under Old Kusu, she'll be — she always is, this time of day.",
      out=[GIVE("ishiro_letter"), STG(SQP, "deliver")], next="f_take_end"),
    N("f_take_end", "postman_fennick", "And... sorry. To her. From me.", [C("Goodbye.")]),
    N("f_active", "postman_fennick",
      "It's in the spiders' hollow, east of the Old Bridge. I heard it go. Rustle-rustle-drag. I'll hear it in my "
      "sleep.", [C("Goodbye.")], cond=[QS(SQP, "find")], fallback="f_greet"),
    N("f_greet", "postman_fennick", "Fennick, Royal Lanternvale Post! Well. Lanternvale Post. Well. Me.",
      [C("You look shaken.", "f_offer"), C("Goodbye.")]),
    N("f_offer", "postman_fennick",
      "A boar charged me on the Whisperwood road! I dropped the satchel and ran, and when I looked back — spiders. "
      "Big ones. They dragged it off to their hollow. Every letter in the valley, in a spider's larder.",
      [C("I'll get it back.", "f_accept", out=[SQ(SQP)]), C("Good luck with that.")]),
    N("f_accept", "postman_fennick",
      "Oh, thank you! If they've eaten the seed catalogues, don't tell Bram. He's been waiting for the turnips.",
      [C("Goodbye.")]),
]))

# ============================================================================ villagers
DLG.append(D("dlg_villager_june", "j_post", [
    N("j_post", "villager_june",
      "The marigolds are blooming twice over! I'm putting them on every lantern in the valley. Twice.",
      [C("Goodbye.")], cond=[F("lanterns_rekindled")], fallback="j_greet"),
    N("j_greet", "villager_june",
      "Marigolds for the lanterns? They like marigolds best. Don't ask me how I know. ...I talk to them.",
      [C("Do they talk back?", "j_talk"),
       CHK("Your flowers are wilting at the edges.", "Nature", 10, "j_nat_s", "j_nat_f", once=True),
       C("Goodbye.")]),
    N("j_talk", "villager_june",
      "Not in words. In warmth. Lately they've gone so quiet I keep checking they're still there.", next="j_greet"),
    N("j_nat_s", "villager_june",
      "*She looks closer.* Grey at the tips... You're right. The Hollow's in the meadow now. *She tucks a marigold "
      "into your collar.* For luck. Keep it in your heart, if not in water.", out=[XP(25)], next="j_greet"),
    N("j_nat_f", "villager_june", "They're just tired, June says, hugging her basket a little tighter.",
      next="j_greet"),
]))
DLG.append(D("dlg_villager_hollis", "hl_post", [
    N("hl_post", "villager_hollis",
      "Hear that creaking? She's turning! First wind in a month, the moment the lanterns lit. Flour for the festival "
      "cakes! I may cry. I may already be crying.", [C("Goodbye.")], cond=[F("lanterns_rekindled")],
      fallback="hl_greet"),
    N("hl_greet", "villager_hollis",
      "The windmill only turns when the lanterns are lit. Daft, isn't it? But it's true. No wind since the great "
      "lantern went out — the sails just hang there.",
      [C("Why would a windmill need lanterns?", "hl_why"), C("Goodbye.")]),
    N("hl_why", "villager_hollis",
      "Because the wind in this valley's a spirit, same as everything else here. Ask Fennel, she'll explain it better. "
      "And longer. Much longer.", [C("Goodbye.")]),
]))
DLG.append(D("dlg_child_toby", "ty_greet", [
    N("ty_greet", "child_toby", "I'm a knight! This is my sword. Its name is Stick.",
      [C("A fine name for a sword.", "ty_name"),
       C("Kneel, Toby. I dub thee Knight of the Well.", "ty_knight", [CLS("Paladin")], tag="PALADIN"),
       C("Is Nell alright?", "ty_nell", [NF("moppet_found")]),
       C("Goodbye.")]),
    N("ty_name", "child_toby",
      "It's a FIERCE name. Stick has slain eleven dragons. They were mostly dandelions. One was a cabbage.",
      [C("Goodbye.")]),
    N("ty_knight", "child_toby",
      "*Toby kneels so fast he falls over.* I'M A REAL KNIGHT, *he informs the entire village.* STICK, WE'RE REAL.",
      [C("Goodbye.")], out=[XP(25)]),
    N("ty_nell", "child_toby",
      "She lost Moppet. She says she's not sad but she is. I gave her my best beetle and she's still sad. That was "
      "my BEST beetle.", [C("Goodbye.")]),
]))
DLG.append(D("dlg_rusk_reformed", "rr_greet", [
    N("rr_greet", "rusk", "Honest work. Feels strange. Good strange.",
      [C("How are you settling in?", "rr_settle"),
       C("You still owe me a toll refund.", "rr_coin", once=True),
       C("Goodbye.")]),
    N("rr_settle", "rusk",
      "Dorrit feeds us till we can't move, and Hollis says my back's worth two mules. Once the lanterns are lit, "
      "Stoneford's fields'll come back. We'll go home in spring.", next="rr_greet"),
    N("rr_coin", "rusk",
      "Ha! *He digs in his coat.* Here. The last coin I didn't spend on seed. Kept it for luck. Reckon you're "
      "luckier than me. Plant something with it.", out=[GIVE("farmers_lucky_coin")], next="rr_greet"),
]))


def build():
    return {"_note": "Village dialogues. Speaker ids are npc ids, 'narrator' or 'player'. Choice tags mark class "
                     "options and skill checks.",
            "dialogues": DLG}
