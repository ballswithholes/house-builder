"""dialogues_companions.json — recruit / party-banter dialogues for the eight companions.

Pattern per companion <c>:
  <p>_party   (InParty c)            banter, lore, dismiss
  <p>_met     (Flag c_met)           quick re-recruit
  <p>1 ...                           first meeting with personality, checks and class options;
                                     there is ALWAYS a check-free path to recruit.
Recruit (engine) sets recruited_<c>; the join node also sets <c>_met. Dismiss clears recruited_<c> so the map
placement (hideFlag recruited_<c>) reappears.
"""
from wn_common import *

DLG = []


def party_block(p, c, greet, banter_post, banter, lore_q, lore_a, dismiss_text, extra_choices=None):
    choices = [C("How are you holding up?", f"{p}_banter_post", [F("lanterns_rekindled")]),
               C("How are you holding up?", f"{p}_banter", [NF("lanterns_rekindled")]),
               C(lore_q, f"{p}_lore")]
    choices += extra_choices or []
    choices += [C("Wait here for now. I'll come back for you.", f"{p}_dismissed",
                  out=[DISMISS(c), CLR(f"recruited_{c}")]),
                C("Let's keep moving.")]
    return [
        N(f"{p}_party", c, greet, choices, cond=[INP(c)], fallback=f"{p}_met"),
        N(f"{p}_banter_post", c, banter_post, next=f"{p}_party_hub"),
        N(f"{p}_banter", c, banter, next=f"{p}_party_hub"),
        N(f"{p}_lore", c, lore_a, next=f"{p}_party_hub"),
        N(f"{p}_party_hub", c, "...Anything else?", choices[2:]),
        N(f"{p}_dismissed", c, dismiss_text),
    ]


def met_block(p, c, text, first):
    return [N(f"{p}_met", c, text,
              [C("Come with me.", f"{p}_join"), C("Just passing by.")],
              cond=[F(f"{c}_met")], fallback=first)]


def join_node(p, c, text):
    return N(f"{p}_join", "narrator", text, out=[RECRUIT(c), SET(f"{c}_met")])


# ============================================================================== Kael
DLG.append(D("dlg_recruit_kael", "ka_party",
    party_block("ka", "kael", "*Kael shifts the greatsword on his shoulder.* We're burning daylight.",
                "The lanterns are lit, and she came home. *Something in his scarred face eases.* I kept my promise. "
                "Strange. I thought it would feel heavier.",
                "I've walked this road before. It ends at a stair, and the stair ends at a choice. This time I'd "
                "rather you were the one making it.",
                "Tell me about Isolde.",
                "She laughed at everything. Bad weather, bad food, me. Even the stair. *A pause.* She didn't laugh "
                "at the top.",
                "Hmph. I'll be at the Sleepy Lantern. Don't take long.",
                [C("What happened at the top of the stair, ten years ago?", "ka_top",
                   [cond("Companion", "kael", amount=20), NF("lanterns_rekindled")])])
    + [N("ka_top", "kael",
         "*He's quiet a long time.* She lit the Heart, and it took everything she had, and she smiled while it did. "
         "I've hated that smile for ten years. *A breath.* ...I think she was smiling at me. Telling me it was "
         "alright.", out=[APP("kael", 3)], next="ka_party_hub")]
    + met_block("ka", "kael", "Back again. Changed your mind, or lost your way?", "ka1")
    + [
        N("ka1", "narrator",
          "*A broad man in a weathered red coat sits on the inn's bench, a greatsword propped beside him like an old "
          "friend. One eye is closed beneath a pale scar; the other watches the road east.*",
          out=[SET("kael_met")], next="ka2"),
        N("ka2", "kael",
          "If you're here about the lanterns, the Elder's under the tree. If you're here about the bench, find "
          "another.",
          [C("Who are you waiting for?", "ka_wait"),
           CHK("You're not resting. You're keeping watch.", "Insight", 12, "ka_ins_s", "ka_ins_f", once=True),
           C("That blade's seen more winters than this village.", "ka_warrior", [CLS("Warrior")], tag="WARRIOR"),
           C("I'm going up to the Old Shrine. Come with me.", "ka_pitch"),
           C("Never mind.")]),
        N("ka2b", "kael", "*He waits for you to say something worth hearing.*",
          [C("Who are you waiting for?", "ka_wait"),
           CHK("You're not resting. You're keeping watch.", "Insight", 12, "ka_ins_s", "ka_ins_f", once=True),
           C("I'm going up to the Old Shrine. Come with me.", "ka_pitch"),
           C("Never mind.")]),
        N("ka_wait", "kael",
          "Someone who'll come down that road when she's ready. Or won't. *He sips from a battered tea flask.* "
          "That's all you're getting.", next="ka2b"),
        N("ka_ins_s", "kael",
          "*He's quiet a long moment.* Ten years ago I walked a summoner up to the Old Shrine. I walked back down "
          "alone. Her daughter's on the same road now. I made a promise.",
          out=[SET("kael_confided"), APP("kael", 5), XP(50)], next="ka_pitch_after"),
        N("ka_ins_f", "kael", "*He looks at you for a long moment.* I'm resting my legs. *He is not.*", next="ka2b"),
        N("ka_warrior", "kael",
          "So have I. *He studies the way you stand.* You hold yours like you mean it. Good. Most don't.",
          out=[APP("kael", 5)], next="ka_pitch_after"),
        N("ka_pitch", "kael", "*He grunts.* The shrine. Everyone's going up the shrine this year.",
          next="ka_pitch_after"),
        N("ka_pitch_after", "kael",
          "If you're climbing that mountain, you'll want someone who's been. I won't promise to be pleasant.",
          [C("Pleasant's overrated. Welcome aboard.", "ka_join", out=[APP("kael", 3)]),
           C("Seren's on the pilgrim road. She could use a guardian.", "ka_seren", [F("seren_met")]),
           CHK("Whatever happened ten years ago, you don't have to carry it up the stair alone.", "Persuasion", 13,
               "ka_pers_s", "ka_pers_f", once=True),
           C("Maybe later.")]),
        N("ka_seren", "kael", "*He's on his feet before you finish.* Then we're late. Move.",
          out=[APP("kael", 10)], next="ka_join"),
        N("ka_pers_s", "kael", "*A long breath out.* ...No. I suppose I don't.", out=[APP("kael", 8)],
          next="ka_join"),
        N("ka_pers_f", "kael", "Words. *But he stands up anyway.* Fine. Someone ought to keep you alive.",
          next="ka_join"),
        join_node("ka", "kael", "*Kael shoulders the greatsword and falls in beside you without another word.*"),
    ]))

# ============================================================================== Aldric
DLG.append(D("dlg_recruit_aldric", "al_party",
    party_block("al", "aldric",
                "Is this the part where we have a meaningful conversation? I've been practising my thoughtful face. "
                "*He demonstrates. It is not thoughtful.*",
                "We saved a whole VALLEY. Do you think they'll write a ballad? I've drafted one. Forty verses, and "
                "they all rhyme with 'Aldric'. Nothing rhymes with 'Aldric'. It's been a struggle.",
                "Brilliant! Terrifying! Mostly brilliant. I've only fallen over twice today, which is a personal "
                "best.",
                "Tell me about your family.",
                "The Sunmeres! Big house, bigger expectations, five older brothers who were better at everything. "
                "I'm the one who left to become a legend. Technically I ran away. Gloriously.",
                "Right! I'll be by the well, being heroic in a standing-still sort of way.")
    + met_block("al", "aldric", "You're back! I knew it. Destiny! Or you forgot something.", "al1")
    + [
        N("al1", "narrator",
          "*A young knight in a gold tabard is practising flourishes with a warhammer, narrating under his breath.* "
          "'...and bold Sir Aldric, undaunted, smote the—' *He notices you and nearly drops it.*",
          out=[SET("aldric_met")], next="al2"),
        N("al2", "aldric",
          "Hello! Aldric Sunmere, knight-errant, slayer of — well, nothing yet, but I'm extremely ready. Are you a "
          "hero? You look like a hero. You've got the boots.",
          [C("I'm going up to the Old Shrine to fix the lanterns.", "al_quest"),
           CHK("That's a line from 'The Lay of Sir Gallant'. The chapbook with the dragon on the cover.", "History",
               13, "al_hist_s", "al_hist_f", once=True),
           C("Another servant of the Light! Walk with me.", "al_paladin", [CLS("Paladin")], tag="PALADIN"),
           CHK("You'll get yourself killed swinging that like a cart handle.", "Intimidation", 10, "al_int_s",
               "al_int_f", once=True),
           C("Good luck with... all that.")]),
        N("al_quest", "aldric",
          "A quest! An actual quest, with a shrine and a curse and probably a monster! *He seizes your hand and pumps "
          "it.* I'm coming. I mean — may I come? I'm coming.", next="al_offer"),
        N("al_hist_s", "aldric",
          "You've READ it! Nobody's read it! *He beams.* I know it's rubbish. The author's a cheesemonger from "
          "Velmora. But Sir Gallant never gave up, and I liked that.", out=[APP("aldric", 8), XP(50)],
          next="al_offer"),
        N("al_hist_f", "aldric", "Aldric frowns. 'It's from my own epic, actually. Which I'm writing. Currently.'",
          next="al_offer"),
        N("al_paladin", "aldric", "Another paladin! Two of us! That's not a party, that's a BALLAD.",
          out=[APP("aldric", 8)], next="al_offer"),
        N("al_int_s", "aldric",
          "*He looks at his grip, then at yours, and adjusts.* ...Oh. Oh, that's MUCH better. You've probably saved "
          "my life already. I'm following you now. That's how it works.", out=[APP("aldric", 5)], next="al_offer"),
        N("al_int_f", "aldric", "Then I'll die gloriously! says Aldric, cheerfully missing the point.",
          next="al_offer"),
        N("al_offer", "aldric",
          "So? Shall I come along? I'm good with a hammer, better with a shield, and excellent at morale.",
          [C("Welcome aboard, Sir Aldric.", "al_join"),
           C("Not right now.", "al_wait")]),
        N("al_wait", "aldric", "Right! I'll be here. Practising. Heroically."),
        join_node("al", "aldric",
                  "*Aldric whoops, salutes, very nearly brains himself with the hammer, and falls into step beside "
                  "you.*"),
    ]))

# ============================================================================== Pip
DLG.append(D("dlg_recruit_pip", "pi_party",
    party_block("pi", "pip", "Ooh, are we talking? I love talking. It's like walking, but with your face.",
                "We fixed the sky-lights! The whole valley's glowing! ...I may have pocketed a tiny bit of the glow. "
                "Kidding! Mostly.",
                "So the Hollow's... not ghosts, right? Definitely not ghosts. I'm fine. This is my fine face.",
                "What's with the goggles?",
                "Cogwright goggles! Three lenses: one for far, one for near, and one for seeing if something's worth "
                "nicking. That one's always on.",
                "Aww. Fine! I'll be by the junk cart, fixing things that aren't broken yet.")
    + met_block("pi", "pip", "Hey, it's you again! Need a scavenger? I come with gadgets AND opinions!", "pi1")
    + [
        N("pi1", "narrator",
          "*A girl in enormous goggles is elbow-deep in a cart full of scrap, humming. Gears, springs and a teapot "
          "fly over her shoulder.*", out=[SET("pip_met")], next="pi2"),
        N("pi2", "pip",
          "Oh! Hi! Pip. Cogwright caravans — scavenger first class, gadgeteer second class, thief— er, finder of "
          "lost things, third class. You look like you're going somewhere exciting!",
          [C("I'm going to the Old Shrine. There may be treasure.", "pi_treasure"),
           CHK("(Try to lift the spanner from her belt.)", "SleightOfHand", 13, "pi_soh_s", "pi_soh_f", once=True),
           C("Finder of lost things? Same. Want to find some together?", "pi_rogue", [CLS("Rogue")], tag="ROGUE"),
           CHK("That gadget in your hand is backwards.", "Investigation", 12, "pi_inv_s", "pi_inv_f", once=True),
           C("I found this shiny button. Want it?", "pi_button", [HAS("junk_shiny_button")],
             out=[TAKE("junk_shiny_button"), APP("pip", 10)]),
           C("Bye, Pip.")]),
        N("pi_treasure", "pip",
          "Treasure? At a shrine? Old shrines ALWAYS have treasure! ...Wait. Old shrines also always have ghosts. "
          "*She swallows.* Are there ghosts?",
          [C("...No?", "pi_lie"), C("Hollowed spirits, yes. I won't lie to you.", "pi_truth")]),
        N("pi_lie", "pip",
          "You're a terrible liar and I love you for trying. Okay! Ghosts! Fine! You go first.",
          out=[APP("pip", 3)], next="pi_offer"),
        N("pi_truth", "pip",
          "Okay. Okay okay okay. Honest. I like honest. I hate ghosts, but I like honest.", out=[APP("pip", 5)],
          next="pi_offer"),
        N("pi_soh_s", "pip",
          "*Your fingers close on the spanner — and find a second spanner already in your pocket.* 'Swapsies!' Pip "
          "crows. 'You're good. I'm better. We should team up!'", out=[APP("pip", 8)], next="pi_offer"),
        N("pi_soh_f", "pip",
          "Nope! *Her hand clamps on your wrist without her even looking up.* Seven out of ten. Lovely technique, "
          "terrible timing.", next="pi_offer"),
        N("pi_rogue", "pip", "A fellow finder! *She fist-bumps you.* Ooh, we're going to find SO many things.",
          out=[APP("pip", 8)], next="pi_offer"),
        N("pi_inv_s", "pip",
          "Is it? *She flips it. It whirrs, sparks, and pours a cup of tea.* IT WAS! You're a genius! We're a team "
          "now. I've decided.", out=[APP("pip", 5), XP(50)], next="pi_offer"),
        N("pi_inv_f", "pip",
          "It's MEANT to be backwards, Pip says, and it explodes, very gently, in a puff of soot. Mostly meant to be.",
          next="pi_offer"),
        N("pi_button", "pip",
          "A BUTTON! Look at it! It's so SHINY! I'm keeping it forever. And I'm coming with you forever. Or at least "
          "until lunch.", next="pi_offer"),
        N("pi_offer", "pip",
          "So! Want a scavenger? I pick locks, I spot traps, I make tea with explosives. Partly with explosives.",
          [C("You're hired.", "pi_join"), C("Maybe later.", "pi_wait")]),
        N("pi_wait", "pip", "I'll be here! Probably! If not, check the junk."),
        join_node("pi", "pip", "*Pip snaps her goggles down over her eyes and bounces into line beside you.*"),
    ]))

# ============================================================================== Rook
DLG.append(D("dlg_recruit_rook", "ro_party",
    party_block("ro", "rook",
                "Tide's happy, I'm happy. Long walk, good company, monsters for exercise. Life's simple, friend.",
                "Lanterns are lit, the woods are singing, and Tide caught a fish nobody believes was that big. Best "
                "season ever, friend. Best season ever.",
                "You hear the woods? Too quiet. Back home we say a quiet reef is holding its breath. I don't like "
                "it, friend.",
                "Tell me about ringball.",
                "Best game in the world! Two teams, one ring, a lot of water and no rules about tackling. I captained "
                "the Saltreach Gulls three seasons. Lost a tooth in the final. Worth it!",
                "Alright! Tide and I'll be back at the camp. Shout if you need a strong arm.")
    + met_block("ro", "rook", "Back again, friend! Tide remembers you — that's his remembering wag.", "ro1")
    + [
        N("ro1", "narrator",
          "*A huge, sun-browned man with beaded hair is grilling a fish over a campfire. A grey wolf with one white "
          "ear lies beside him, tail thumping.*", out=[SET("rook_met")], next="ro2"),
        N("ro2", "rook",
          "Ho, friend! Sit, sit — fish is nearly done. I'm Rook, from the Saltreach Isles, and this handsome devil is "
          "Tide. Don't mind him. He only bites fish, and people I point at.",
          [C("What's an islander doing in Whisperwood?", "ro_why"),
           CHK("(Catch the ball he suddenly lobs at your head.)", "Athletics", 12, "ro_ath_s", "ro_ath_f",
               once=True),
           CHK("Those tracks by your camp aren't Tide's. Something's been circling.", "Survival", 11, "ro_surv_s",
               "ro_surv_f", once=True),
           C("A fine wolf. Strong shoulders, kind eyes.", "ro_hunter", [CLS("Hunter")], tag="HUNTER"),
           C("The Hollow's spreading. I'm heading deeper in.", "ro_hollow"),
           C("Enjoy your fish.")]),
        N("ro_why", "rook",
          "Came looking for the best fishing in the world! Found it, too — the river past the Old Bridge. Then the "
          "grey came, and the fish went grey, and Tide won't eat them. Neither will I.", next="ro_hollow"),
        N("ro_ath_s", "rook",
          "*You snatch it out of the air one-handed.* HA! Reflexes! You'd make a fine ringball player, friend. I'd "
          "put you on the wing.", out=[APP("rook", 8)], next="ro_offer"),
        N("ro_ath_f", "rook",
          "*The ball bonks you on the forehead. Tide wags apologetically.* Ahh, sorry, sorry! Habit! Back home that's "
          "how we say hello.", next="ro_offer"),
        N("ro_surv_s", "rook",
          "*Rook's grin fades.* Aye. Big one, and grey. Came by three nights running. Tide's been sleeping with one "
          "eye open, and so have I. *He looks at you with new respect.*", out=[APP("rook", 5), XP(50)],
          next="ro_hollow"),
        N("ro_surv_f", "rook", "Those are mine, says Rook. I walk funny after beans.", next="ro_hollow"),
        N("ro_hunter", "rook",
          "Isn't he? *Rook beams like a proud father.* Raised him from a pup. He's smarter than me, but don't tell "
          "him.", out=[APP("rook", 8)], next="ro_offer"),
        N("ro_hollow", "rook",
          "This grey — it's in the river, the trees, the beasts. Back home, when the reef goes sick, everyone pulls "
          "together. Nobody's pulling together here. Everyone's just scared.", next="ro_offer"),
        N("ro_offer", "rook",
          "So how about it? You need a bow, a strong back and a wolf who's better than both? We're yours, friend.",
          [C("Welcome aboard, Rook. And Tide.", "ro_join"), C("Not just yet.", "ro_wait")]),
        N("ro_wait", "rook", "Fish'll be here. So will we."),
        join_node("ro", "rook",
                  "*Rook stamps out the fire, slings his bow over his shoulder and whistles. Tide bounds to his "
                  "side, tail up like a flag.*"),
    ]))

# ============================================================================== Seren
DLG.append(D("dlg_recruit_seren", "se_party",
    party_block("se", "seren",
                "*Her ringed staff chimes softly as she walks.* Is everything alright? You look like you're carrying "
                "something heavy.",
                "*She looks up at the lanterns. Her eyes are wet, and she is smiling.* I came up this road to end my "
                "pilgrimage. I think I've only just begun it.",
                "My mother walked this road ten years ago. I keep finding her in it — a ribbon on a branch, a prayer "
                "scratched into a stone. I think she wanted me to know she wasn't afraid. ...I am, a little.",
                "Tell me about your pilgrimage.",
                "A summoner visits every shrine in the valley and rekindles its lantern before she climbs to the "
                "Heart. I've lit six. The Wayside lantern wouldn't take. None will, now, until the Heart burns again.",
                "Of course. I'll wait at the Wayside Shrine. Komorebi likes the company.",
                [C("What will you do at the top of the stair?", "se_top",
                   [cond("Companion", "seren", amount=20), NF("lanterns_rekindled")])])
    + [N("se_top", "seren",
         "Kneel, pray, and give the Heart what it asks. That's the rite. *She turns the staff in her hands until its "
         "rings chime.* ...But if you find another way before we get there — please. Please tell me.",
         out=[SET("seren_hopes"), APP("seren", 3)], next="se_party_hub")]
    + met_block("se", "seren", "You came back. *She smiles.* Komorebi said you would.", "se1")
    + [
        N("se1", "narrator",
          "*A young woman in pale blue summoner's robes kneels before the dark Wayside lantern, a ringed staff "
          "across her knees. Her hair is dark, with a single white streak at the temple.*",
          out=[SET("seren_met")], next="se2"),
        N("se2", "seren",
          "Oh! I'm sorry, I didn't hear you. I'm Seren. I'm a summoner, on pilgrimage to rekindle the shrines. *She "
          "glances at the lantern.* It isn't going very well.",
          [C("Why won't it light?", "se_why"),
           CHK("(Kneel beside her and pray with her.)", "Religion", 12, "se_rel_s", "se_rel_f", once=True),
           CHK("You're afraid of what's waiting at the top of the stair.", "Insight", 13, "se_ins_s", "se_ins_f",
               once=True),
           C("Kael is with me.", "se_kael", [INP("kael")]),
           C("I'm going up to the Old Shrine too.", "se_together")]),
        N("se_why", "seren",
          "The little shrines borrow their light from the Heart Lantern, up the stair. If the Heart is failing, "
          "nothing I do down here will hold. *She squares her shoulders.* So I have to go up. That was always the "
          "plan.", next="se_together"),
        N("se_rel_s", "narrator",
          "*You kneel. Together you murmur the pilgrim's prayer, and for a moment the Wayside lantern flickers — a "
          "tiny, defiant gold — before it fades.* Seren stares at you. 'It answered. It answered both of us.'",
          out=[APP("seren", 8), XP(75)], next="se_together"),
        N("se_rel_f", "seren",
          "*You stumble over the words. Seren gently finishes the line for you.* It's alright. The spirits listen to "
          "the meaning, not the grammar.", out=[APP("seren", 2)], next="se_together"),
        N("se_ins_s", "seren",
          "*Her hands tighten on the staff.* My mother climbed the stair ten years ago and gave the Heart Lantern "
          "everything she had. Everyone says it's the bravest thing a summoner can do. *Quietly.* I don't want to be "
          "brave like that. But I will be, if I have to.", out=[SET("seren_confided"), APP("seren", 5), XP(50)],
          next="se_together"),
        N("se_ins_f", "seren", "Afraid? No. *She smiles brightly. It almost reaches her eyes.*", next="se_together"),
        N("se_kael", "seren",
          "Kael? *She's on her feet.* Kael! You never answered my letters. Not one, in ten years.", next="se_kael2"),
        N("se_kael2", "kael", "I'm answering now. I made your mother a promise. I'm keeping it.",
          out=[APP("seren", 5), APP("kael", 5)], next="se_together"),
        N("se_together", "seren",
          "A summoner should have guardians on the pilgrim road. It's tradition. *She hesitates, then smiles — "
          "properly, this time.* Would you be mine? I can heal, I can pray, and I'm much braver with company.",
          [C("I'd be honoured, Seren.", "se_join", out=[APP("seren", 3)]),
           C("Guardian? I'm more of a 'hit things' sort.", "se_funny"),
           C("Not yet.", "se_wait")]),
        N("se_funny", "seren", "Guardians hit things all the time. It's most of the job, Kael says.",
          next="se_join"),
        N("se_wait", "seren", "Then I'll keep praying. Come back when you're ready — I'll be here."),
        join_node("se", "seren",
                  "*Seren rises, brushes the moss from her robes and lifts her staff. Its rings chime, clear and "
                  "bright, like the first step of a long road.*"),
    ]))

# ============================================================================== Lys
DLG.append(D("dlg_recruit_lys", "ly_party",
    party_block("ly", "lys",
                "If you're going to ask how I'm feeling, don't. If you're going to ask about the Hollow, do.",
                "The lanterns are lit, the Hollow has receded, and I have three notebooks of observations nobody will "
                "believe. *Beat.* ...It was well done. Don't make me say it twice.",
                "Fascinating stuff, this blight. It doesn't destroy light — it inverts it. Light turned inside out. If "
                "I weren't standing in it, I'd be delighted.",
                "What's in the pressed-flower book?",
                "*She snaps it shut.* Research. *A pause.* ...Marigolds. From Lanternvale. Tell anyone and I will turn "
                "you into a newt. I've been practising.",
                "Fine. I'll be at the grove, taking notes. Try not to die before I've finished them.")
    + met_block("ly", "lys", "You again. Have you read a book since we last spoke?", "ly1")
    + [
        N("ly1", "narrator",
          "*A tall woman in a black dress made almost entirely of belts stands at the edge of the grey, scribbling in "
          "a notebook. A staff with a dark, storm-filled orb leans against a dead tree beside her.*",
          out=[SET("lys_met")], next="ly2"),
        N("ly2", "lys",
          "Don't step on the specimens. *She doesn't look up.* Lys. Arcanum of Velmora. If you're lost, Lanternvale's "
          "west. If you're a pilgrim, you're late. If you're a hero, you're tediously early.",
          [CHK("The blight isn't darkness. It's light turned inside out — that's why the crystals grow where "
               "lanterns die.", "Arcana", 13, "ly_arc_s", "ly_arc_f", once=True),
           CHK("Velmora's Arcanum hasn't sent anyone to Lanternvale in a century. Why you?", "History", 12,
               "ly_hist_s", "ly_hist_f", once=True),
           C("Professional courtesy, colleague. Mind if I look at your notes?", "ly_mage", [CLS("Mage")],
             tag="MAGE"),
           C("Your Arcanum would call this fel corruption and burn the whole grove.", "ly_warlock",
             [CLS("Warlock")], tag="WARLOCK"),
           C("I'm going to the Old Shrine to stop this.", "ly_shrine"),
           C("Sorry to bother you.")]),
        N("ly_arc_s", "lys",
          "*Her pen stops. She looks at you for the first time.* ...Inverted. Yes. Exactly that. *Something almost "
          "like a smile.* How irritating. I was going to publish that.", out=[APP("lys", 10), XP(75)],
          next="ly_offer"),
        N("ly_arc_f", "lys",
          "You've strung three correct words into an incorrect sentence. Impressive, in its way.", next="ly_shrine"),
        N("ly_hist_s", "lys",
          "*A long pause.* They didn't send me. I came. I grew up here, before the Arcanum. Seren was my— *She stops.* "
          "It's none of your concern.", out=[SET("lys_confided"), APP("lys", 5), XP(50)], next="ly_shrine"),
        N("ly_hist_f", "lys", "Because I'm the best they have, says Lys, which is not an answer.",
          next="ly_shrine"),
        N("ly_mage", "lys",
          "*She hands you the notebook without a word. Her notes are meticulous, annotated, and occasionally rude.* "
          "...You may continue to exist near me.", out=[APP("lys", 8)], next="ly_offer"),
        N("ly_warlock", "lys",
          "They would. Fools. *She eyes you.* And you'd want to bottle it. Also a fool, but a more interesting one.",
          out=[APP("lys", 3)], next="ly_offer"),
        N("ly_shrine", "lys",
          "Stop this. *She closes the notebook.* Every observation I've made points up the stair. I've been waiting "
          "for someone sufficiently reckless to escort me.", next="ly_offer"),
        N("ly_offer", "lys",
          "I'll come. Not because I like you. Because the Hollow needs studying at its source, and you need someone "
          "who can set things on fire precisely.",
          [C("Glad to have you, Lys.", "ly_join"),
           C("Seren will be glad to see you.", "ly_seren", [INP("seren")]),
           C("Another time.", "ly_wait")]),
        N("ly_seren", "seren", "*Softly.* Hello, Lys.", next="ly_seren2"),
        N("ly_seren2", "lys",
          "*Her composure cracks for exactly half a second.* ...Seren. *She clears her throat.* Well. Someone has to "
          "keep you alive. Again.", out=[APP("lys", 10), APP("seren", 5)], next="ly_join"),
        N("ly_wait", "lys", "I'll be here. With the crystals. They're better conversationalists."),
        join_node("ly", "lys",
                  "*Lys tucks the notebook into one of her many belts and takes up her staff. Inside the orb, a "
                  "small storm wakes and pays attention.*"),
    ]))

# ============================================================================== Torvan
DLG.append(D("dlg_recruit_torvan", "to_party",
    party_block("to", "torvan", "I am listening.",
                "The Great One walks again. The Hornkin's shame is lifted. *He touches the cords braided on his horn.* "
                "Tonight I will braid a new cord. For you.",
                "The mountain is angry. The wind tastes of ash. We climb anyway, because we must.",
                "Tell me of the Hornkin.",
                "My people live above the snowline. For a thousand years we guarded the Warden's herd: deer, goats, "
                "the small spirits of the high meadows. When the Warden fell, I was on watch. I came down alone, to "
                "set it right.",
                "Then I will guard the stair until you call.")
    + met_block("to", "torvan", "You return. Good.", "to1")
    + [
        N("to1", "narrator",
          "*At the foot of the shrine stair stands a giant: a horned beast-folk with silver-grey fur, a leaf-bladed "
          "staff planted at his side. Charms and braided cords hang from his great curled horns.*",
          out=[SET("torvan_met")], next="to2"),
        N("to2", "torvan",
          "Stop. *His voice is deep as a landslide.* I am Torvan of the Hornkin. The stair above is grey and hungry. "
          "None pass who cannot stand against it.",
          [CHK("(Plant your feet and meet his challenge, shoulder to shoulder.)", "Athletics", 14, "to_ath_s",
               "to_ath_f", once=True),
           CHK("The Hornkin guarded the Warden's herd. You blame yourself for what happened to him.", "Nature", 12,
               "to_nat_s", "to_nat_f", once=True),
           C("(Touch your totem to his horn-charm and greet him in the old way.)", "to_shaman", [CLS("Shaman")],
             tag="SHAMAN"),
           CHK("You're not guarding the stair to keep us out. You're afraid to climb it alone.", "Insight", 13,
               "to_ins_s", "to_ins_f", once=True),
           C("We're here to free the Warden, not to fight you.", "to_free")]),
        N("to_ath_s", "torvan",
          "*He pushes. You push back. The ground creaks. Then Torvan steps aside and inclines his great head.* You "
          "stand. Good.", out=[APP("torvan", 10)], next="to_offer"),
        N("to_ath_f", "torvan",
          "*He leans, gently, and you slide back three paces through the moss.* ...You are small. But you did not "
          "fall. That is something.", next="to_free"),
        N("to_nat_s", "torvan",
          "*His ears flatten.* ...Yes. I was on watch the night the grey took him. I heard him cry out, and I did not "
          "understand. A Hornkin should have understood.", out=[APP("torvan", 8), XP(75)], next="to_offer"),
        N("to_nat_f", "torvan", "You know nothing of the Hornkin, Torvan rumbles. He is not angry. Only tired.",
          next="to_free"),
        N("to_shaman", "torvan",
          "*His eyes widen. Gravely, he touches his horn-charm to your totem.* Spirit-speaker. Then you hear it too — "
          "the Warden, crying. Yes. We go together.", out=[APP("torvan", 10)], next="to_offer"),
        N("to_ins_s", "torvan",
          "*A long, heavy silence.* ...Yes. Alone, I would fail him twice. With you, perhaps not.",
          out=[APP("torvan", 5), XP(50)], next="to_offer"),
        N("to_ins_f", "torvan", "I fear nothing, Torvan says. His tail flicks, once.", next="to_free"),
        N("to_free", "torvan",
          "Free him. *He considers you for a long moment.* Words are light. Deeds are heavy. Show me.",
          next="to_offer"),
        N("to_offer", "torvan",
          "I will climb with you. My staff, my spirits and my horns are yours, until the Warden walks again.",
          [C("Climb with us, Torvan.", "to_join"), C("Not yet.", "to_wait")]),
        N("to_wait", "torvan", "Then I will wait. I have waited a long time. A little longer is nothing."),
        join_node("to", "torvan",
                  "*Torvan pulls his staff from the earth. The charms on his horns clack softly, like distant "
                  "stones.*"),
    ]))

# ============================================================================== Morwen
DLG.append(D("dlg_recruit_morwen", "mo_party",
    party_block("mo", "morwen",
                "Yes, darling? *A tiny imp peers over her shoulder, then ducks back.* Dear and I were just discussing "
                "you. Favourably, mostly.",
                "A great spirit restored, a valley relit, and not a single demon involved. How quaint. *She smiles.* "
                "I had a lovely time.",
                "I came for a demonic incursion and found a very sad deer. I'm told this is called 'character "
                "growth'.",
                "Who is 'Dear'?",
                "My imp. Small, rude, devoted. He has a proper name, of course, but it's forty syllables and half of "
                "them are on fire. 'Dear' is simpler.",
                "As you wish. I'll be among the ruins. Do fetch me before anything interesting happens.")
    + met_block("mo", "morwen", "Ah. The persistent one. How nice.", "mo1")
    + [
        N("mo1", "narrator",
          "*Among the broken pillars, a tall woman in a high-collared violet coat is sketching a blight crystal in a "
          "leather grimoire. A tiny red imp sits on her shoulder, holding the inkpot.*",
          out=[SET("morwen_met")], next="mo2"),
        N("mo2", "morwen",
          "Visitors! How lovely. Morwen Vale-Ashcombe — scholar, collector, occasional correspondent with things one "
          "shouldn't write to. And this is Dear. Say hello, Dear. *The imp hisses.* He's shy.",
          [CHK("These crystals aren't fel. There's no demonic signature here at all.", "Arcana", 14, "mo_arc_s",
               "mo_arc_f", once=True),
           CHK("Keep that imp on a leash, or I'll do it for you.", "Intimidation", 13, "mo_int_s", "mo_int_f",
               once=True),
           C("A fellow correspondent. What brings you somewhere so dull and demon-free?", "mo_warlock",
             [CLS("Warlock")], tag="WARLOCK"),
           CHK("This is sacred ground. Your kind of study isn't welcome here.", "Religion", 10, "mo_rel_s",
               "mo_rel_f", once=True),
           C("What are you doing here?", "mo_why")]),
        N("mo_why", "morwen",
          "I heard rumours of a grey blight that hollows out spirits, and naturally I assumed demons. I was so "
          "excited. *She sighs.* It isn't demons. It's grief, or hunger, or some tedious spiritual malaise. Still — a "
          "shrine-god gone feral is not nothing.", next="mo_offer"),
        N("mo_arc_s", "morwen",
          "*She closes the grimoire with a soft clap.* No. There isn't, is there? I've stared at these for three days "
          "hoping I was wrong. You've a precise mind. I collect those.", out=[APP("morwen", 10), XP(75)],
          next="mo_offer"),
        N("mo_arc_f", "morwen", "Hm. A theory, Morwen says kindly. Not a good one. But a theory.", next="mo_why"),
        N("mo_int_s", "morwen",
          "*Dear dives into her coat. Morwen laughs, delighted.* Oh, I like you. Nobody threatens anyone properly any "
          "more. It's refreshing.", out=[APP("morwen", 8)], next="mo_offer"),
        N("mo_int_f", "morwen",
          "Dear, says Morwen mildly. The imp grins and sets your bootlace on fire. Shall we try that again, with "
          "manners?", next="mo_why"),
        N("mo_warlock", "morwen",
          "A colleague! How marvellous. We must compare pacts over tea. *Dear mutters something sulky in a language "
          "made of sparks.*", out=[APP("morwen", 10)], next="mo_offer"),
        N("mo_rel_s", "morwen",
          "Sacred ground, she repeats, amused. Darling, I have been terribly respectful. I've only taken samples of "
          "the parts already dead.", out=[APP("morwen", -3)], next="mo_why"),
        N("mo_rel_f", "morwen", "Unwelcome? I'm a delight. *Dear nods vigorously.*", next="mo_why"),
        N("mo_offer", "morwen",
          "You're going up to the sanctum, aren't you? To face whatever sits at the heart of all this grey. I should "
          "very much like to watch. And help, of course. I'm extremely helpful when I'm interested.",
          [C("Your help is welcome.", "mo_join"),
           C("Watch, or help?", "mo_both"),
           C("No, thank you.", "mo_wait")]),
        N("mo_both", "morwen", "Both, darling. I'm a scholar. Watching is how I help.",
          [C("...Fine. Come along.", "mo_join"), C("No, thank you.", "mo_wait")]),
        N("mo_wait", "morwen", "Pity. I'll be here, should you change your mind. Dear will be sulking."),
        join_node("mo", "morwen",
                  "*Morwen tucks her grimoire under one arm. Dear scrambles back onto her shoulder and blows a tiny, "
                  "smug smoke ring at you.*"),
    ]))


def build():
    return {"_note": "Companion recruit and party dialogues (CompanionDef.recruitDialogue). The same dialogue is used "
                     "when talking to a companion who is already in the party.",
            "dialogues": DLG}
