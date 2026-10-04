"""dialogues_whisperwood.json and dialogues_shrine.json"""
from wn_common import *

MQ = "mq_lanterns"
WW, SH = [], []

# ============================================================================ Komorebi
WW.append(D("dlg_komorebi", "k_post", [
    N("k_post", "komorebi",
      "*The Wayside lantern blazes gold, and Komorebi spins inside it like a leaf in a sunbeam.* You did it! I can "
      "feel the Heart from here, warm as a hand on my back. Thank you, little lights.", [C("Goodbye.")],
      cond=[F("lanterns_rekindled")], fallback="k_after"),
    N("k_after", "komorebi",
      "The taper burns brighter now, doesn't it? Rotheart's roots seal the stair at the eastern edge of the wood. "
      "Burn through, little light. I'll keep this lantern warm as long as I can.", [C("Goodbye.")],
      cond=[F("embers_gathered")], fallback="k_ret"),
    N("k_ret", "komorebi",
      "You found them! Oh — oh, they're shivering. Quickly, hold the taper close.",
      [C("(Offer the Spirit Embers to the Kindling Taper.)", "k_emb2",
         out=[TAKE("spirit_ember", 3), SET("embers_gathered"), STG(MQ, "rotheart"), XP(150)])],
      cond=[F("met_komorebi"), HAS("spirit_ember", 3), QA(MQ)], fallback="k_embers"),
    N("k_emb2", "narrator",
      "*The embers drift into the Kindling Taper's flame. It flares warm and gold, and for a moment the dark Wayside "
      "lantern flickers in answer.*", next="k_emb3"),
    N("k_emb3", "komorebi",
      "There. Now the taper carries their light too. The Hollow has a heart in this wood: Rotheart, the old "
      "stair-guardian. His grey roots seal the way to the shrine, but with that flame his thorns will burn.",
      next="k_emb4"),
    N("k_emb4", "komorebi",
      "Be gentle with him, if you can. He used to let the village children climb him on festival nights.",
      [C("I'll do what I can.")]),
    N("k_embers", "komorebi",
      "The wisps drift in the Grey Grove, past the Old Bridge. Three embers, little light. They want to come home; "
      "they just don't remember how.", [C("I'll bring them.")],
      cond=[F("met_komorebi"), QA(MQ)], fallback="k_first"),
    N("k_first", "narrator",
      "*A small spirit, round as a dandelion clock, peers out of the dark lantern. Her light is barely a candle's.*",
      out=[SET("met_komorebi")], cond=[QA(MQ), F("met_elder")], fallback="k_noquest", next="k_f1"),
    N("k_f1", "komorebi",
      "Oh! A traveller. With... is that a Kindling Taper? Then Maru sent you. Good. Good. I'm Komorebi. I keep the "
      "Wayside lantern for pilgrims. Or I did.", next="k_f2"),
    N("k_f2", "komorebi",
      "The Warden stopped coming. Then the light from the Heart stopped coming. And now the Hollow is eating my "
      "forest, one wisp at a time.",
      [C("What happened to the Warden?", "k_warden"),
       CHK("The Kindling. The Heart Lantern needs a new flame, doesn't it?", "Religion", 12, "k_rel_s", "k_rel_f",
           once=True),
       CHK("This taper holds hearth-fire. Could it hold other lights too?", "Arcana", 13, "k_arc_s", "k_arc_f",
           once=True),
       C("How can I help?", "k_f3")]),
    N("k_warden", "komorebi",
      "He's hungry. Lanterns are spirit, and so is he, and when the Heart ran low he started drinking from it "
      "instead of giving. The more he drinks, the greyer he gets. I don't think he can stop.", next="k_f2b"),
    N("k_f2b", "komorebi", "*She flickers anxiously.*",
      [CHK("The Kindling. The Heart Lantern needs a new flame, doesn't it?", "Religion", 12, "k_rel_s", "k_rel_f",
           once=True),
       CHK("This taper holds hearth-fire. Could it hold other lights too?", "Arcana", 13, "k_arc_s", "k_arc_f",
           once=True),
       C("How can I help?", "k_f3")]),
    N("k_rel_s", "komorebi",
      "Yes! Every ten years. Isolde gave hers, and it was a good, bright flame... but one person's light can only "
      "last so long. There must be another way. There must.", out=[SET("knows_kindling_cost"), XP(75)],
      next="k_f2b"),
    N("k_rel_f", "komorebi", "*Komorebi tilts, like a puzzled flower.* It isn't that simple, little light.",
      next="k_f2b"),
    N("k_arc_s", "komorebi",
      "Oh, clever! It can! It's a vessel, not a flame. If you fed it the light the Hollow stole... *She spins in a "
      "little circle of delight.*", out=[SET("taper_insight"), XP(75)], next="k_f3"),
    N("k_arc_f", "komorebi", "*Komorebi blinks.* Tapers are for burning, silly.", next="k_f2b"),
    N("k_f3", "komorebi",
      "The Hollow wisps in the Grey Grove are lantern-flames it stole and emptied. Free their embers and bring them "
      "here, and I'll feed them to your taper. A taper carrying the valley's light might burn through anything. "
      "Three embers. Please.",
      [C("I've already freed three embers. Here.", "k_emb2", [HAS("spirit_ember", 3)],
         out=[TAKE("spirit_ember", 3), SET("embers_gathered"), STG(MQ, "rotheart"), XP(150)]),
       C("I'll bring them.", "k_f4")]),
    N("k_f4", "komorebi",
      "Thank you. Go east, past the Old Bridge. And little light — the wisps aren't wicked. They're lost. Be quick, "
      "and be kind.", [C("Goodbye.")]),
    N("k_noquest", "komorebi",
      "*A tiny spirit peeks out of the dark lantern and whispers:* A pilgrim? No taper... Go and see Maru in the "
      "village first, little light. She'll know what to do.", [C("Goodbye.")]),
]))

# ============================================================================ Moppet
SQN = "sq_spirit_friend"
WW.append(D("dlg_moppet", "m_home", [
    N("m_home", "moppet",
      "*Moppet is curled up as close to Nell as a spirit can get. He jingles at you, happily and slightly sticky "
      "with honey.*", [C("Goodbye.")], cond=[F("moppet_found")], fallback="m_find"),
    N("m_find", "narrator",
      "*Under the glowing mushrooms, a tiny spirit with a leaf on its head is shivering. When it sees you, it "
      "jingles in alarm and ducks out of sight.*",
      [C("(Ring Nell's bell.)", "m_bell", [HAS("nells_bell")]),
       CHK("(Sit very still and let him come to you.)", "Nature", 12, "m_nature", "m_fail", once=True),
       CHK("(Hum the pilgrim's song Komorebi sings.)", "Religion", 11, "m_success", "m_fail", once=True),
       C("(Offer him a honey cake.)", "m_cake", [HAS("food_honey_cake")], out=[TAKE("food_honey_cake")]),
       C("(Speak to him as the spirits speak.)", "m_success", [CLS("Shaman")], tag="SHAMAN"),
       C("(Offer him a little of your own light.)", "m_success", [CLS("Priest")], tag="PRIEST"),
       C("(Leave him be for now.)")],
      cond=[QA(SQN)], fallback="m_hide"),
    N("m_hide", "narrator",
      "*Something small and green is hiding under the glowing mushrooms. It peeks at you, jingles in alarm and "
      "vanishes beneath a cap. It seems to be waiting for someone it knows.*", [C("(Leave it be.)")]),
    N("m_bell", "narrator",
      "*The little bell rings out, clear as morning. Moppet's head pops up. He jingles — a question.*",
      [C("Nell sent me. She misses you.", "m_success"),
       CHK("The lanterns will be lit again, I promise. You don't have to be scared.", "Persuasion", 10,
           "m_success", "m_fail", once=True, out=[APP("seren", 2)])]),
    N("m_fail", "narrator",
      "*Moppet squeaks and burrows deeper under the mushrooms. Perhaps something gentler — or something sweeter.*",
      next="m_find"),
    N("m_cake", "narrator",
      "*Moppet inches forward... snatches the cake... and devours it in a shower of crumbs. He jingles. It is "
      "unmistakably 'MORE'.*", next="m_success"),
    N("m_nature", "narrator",
      "*You sit. You wait. A moth lands on your knee. Then a tiny weight lands on the other one. Moppet looks up, "
      "decides you'll do, and climbs aboard.*", next="m_secret"),
    N("m_success", "narrator",
      "*Moppet tumbles out from under the mushroom and leaps onto your shoulder, jingling like a sleigh full of "
      "bells. He smells of rain and moss.*", next="m_secret"),
    N("m_secret", "narrator",
      "*Before you go, Moppet tugs your sleeve toward an old stump at the edge of the ring and jingles very "
      "seriously. Something is buried there — a secret, and he has decided you may have it.*",
      out=[SET("moppet_found"), SET("moppet_secret"), XP(100)], next="m_end"),
    N("m_end", "narrator", "*He settles on your shoulder. Time to take him home to Nell.*", [C("(Continue.)")]),
]))

# ============================================================================ Puddlecap (encounter)
ENC_M = "enc_mossling_camp"
CAMP_DONE = "mossling_camp_done"
WW.append(D("dlg_puddlecap", "p1", [
    N("p1", "narrator",
      "*A ring of tiny tents made of leaves. In the middle, a little lantern made from a thimble burns a stolen wick. "
      "Mosslings scatter, brandishing twigs. The biggest one, in the biggest hat, steps forward.*", next="p2"),
    N("p2", "puddlecap",
      "Big-folk! Stop! This is Puddlecap Hollow, and Puddlecap is chief, and these wicks are ours. We found them. In "
      "a shed. Behind a lock. Which we also found.",
      [C("Those wicks belong to Tobben the lamplighter.", "p3"),
       CHK("That lantern you've made... you're trying to keep the Hollow away, aren't you?", "Nature", 12,
           "p_nat_s", "p_nat_f", once=True),
       C("(Speak in the slow, damp language of moss and stone.)", "p_shaman", [CLS("Shaman")], tag="SHAMAN"),
       CHK("Hand them over. Now.", "Intimidation", 14, "p_int_s", "p_threat", once=True),
       C("(Attack.)", "p_attack")]),
    N("p3", "puddlecap",
      "Tobben! Tobben has hundreds of wicks! A whole shed of light! Mosslings had one mushroom that glowed, and the "
      "grey ate it.",
      [CHK("If I ask Tobben to hang a lantern for you at the forest edge — a real one — will you give the wicks "
           "back?", "Persuasion", 13, "p_deal", "p_pers_f", once=True),
       CHK("You're not thieves. You're frightened.", "Insight", 11, "p_ins_s", "p_ins_f", once=True),
       C("Give them back, or we take them.", "p_threat"),
       C("(Attack.)", "p_attack")]),
    N("p_nat_s", "puddlecap",
      "*Puddlecap's ears droop.* The grey came to the mushroom ring. Ate the glow right out of it. Mosslings need "
      "light too, big-folk. Little light. Just a little.", out=[XP(50)], next="p_offer"),
    N("p_nat_f", "puddlecap",
      "*You suggest the lantern is decorative. Puddlecap looks deeply offended on the lantern's behalf.*",
      next="p3"),
    N("p_shaman", "narrator",
      "*You speak in the slow, damp tongue of moss. Every Mossling freezes. Puddlecap removes his enormous hat.* "
      "'Old-speaker. We did not know. Forgive.'", next="p_deal"),
    N("p_ins_s", "puddlecap",
      "*His lip wobbles.* Not frightened. Mosslings are BRAVE. ...Mosslings are a bit frightened.", next="p_offer"),
    N("p_ins_f", "puddlecap", "Frightened? Of BIG-FOLK? Ha! Ha. *Several Mosslings hide behind him.*",
      next="p3"),
    N("p_offer", "puddlecap",
      "If big-folk promise light for Mosslings... maybe Mosslings give back wicks. Maybe. Promise?",
      [C("I promise. Tobben will hang a lantern for you.", "p_deal"),
       C("No deals.", "p_threat")]),
    N("p_pers_f", "puddlecap",
      "Big-folk promises are like big-folk feet. They squash. *The Mosslings raise their twigs.*",
      [C("Then we do this the hard way.", "p_threat"),
       CHK("You're not thieves. You're frightened.", "Insight", 11, "p_ins_s", "p_ins_f", once=True)]),
    N("p_int_s", "puddlecap",
      "*Puddlecap looks at your weapon. Then at his twig. Then, sulkily, at the wicks.* Fine. FINE. Take them. "
      "Mosslings are moving to a nicer forest. With nicer big-folk.",
      out=[GIVE("lantern_wick", 6), SET("mosslings_scared"), SET(CAMP_DONE), APP("morwen", 3), APP("seren", -3),
           APP("pip", -2), XP(150)]),
    N("p_threat", "puddlecap", "Threats! In Puddlecap's own hollow! Mosslings — DEFEND THE THIMBLE!",
      out=[APP("morwen", 2), APP("seren", -3), FIGHT(ENC_M)]),
    N("p_attack", "narrator", "*Puddlecap squeaks in outrage. A dozen twigs are raised as one.*",
      out=[APP("seren", -5), APP("rook", -3), FIGHT(ENC_M)]),
    N("p_deal", "puddlecap",
      "Then it's a deal, big-folk! Mosslings keep promises. Mostly. Here — six wicks, only a bit nibbled. And this: "
      "Puddlecap's second-best hat, for a friend.",
      out=[GIVE("lantern_wick", 6), GIVE("puddlecaps_mushroom_hat"), SET("mosslings_befriended"), SET(CAMP_DONE),
           APP("seren", 5), APP("rook", 3), APP("pip", 3), XP(200)], next="p_end"),
    N("p_end", "narrator",
      "*The Mosslings cheer, then immediately start arguing over who gets to sit nearest the thimble-lantern.*",
      [C("(Leave them to it.)")]),
]))

# ============================================================================ bandit lookouts (encounter)
ENC_L = "enc_bandit_lookouts"
WW.append(D("dlg_bandit_lookouts", "l_peace", [
    N("l_peace", "bandit_lookout",
      "Oh — it's you. Rusk says you're alright. Go on through, friend. Mind the loose plank.",
      out=[SET("lookouts_talked"), SET("lookouts_done")], cond=[F("bandits_peaceful")], fallback="l1",
      choices=[C("(Continue.)")]),
    N("l1", "bandit_lookout",
      "Oi! Stop there. Road's closed. Well, not closed. Tolled. Toll's at the bridge, and Rusk does the talking. Off "
      "you go, and keep your hands where we can see 'em.",
      [C("Fine. Let's go and see Rusk.", "l_pass", out=[SET("lookouts_talked"), SET("lookouts_done")]),
       CHK("You two don't look like you've ever robbed anyone.", "Insight", 12, "l_ins_s", "l_ins_f", once=True),
       C("Get out of our way.", "l_fight")]),
    N("l_pass", "bandit_lookout", "Good. Sensible. You've got a sensible face.", [C("(Continue.)")]),
    N("l_ins_s", "bandit_lookout",
      "*The younger one lowers his bow.* We haven't, he admits. We're from Stoneford. We grow barley. Don't tell "
      "Rusk I said.", out=[SET("lookouts_talked"), SET("lookouts_done"), SET("bandits_are_farmers"), XP(50)],
      choices=[C("(Continue.)")]),
    N("l_ins_f", "bandit_lookout", "Have SO, says the younger one, and draws.", out=[FIGHT(ENC_L)]),
    N("l_fight", "bandit_lookout", "Right, then. Rusk won't like this.", out=[APP("seren", -2), FIGHT(ENC_L)]),
]))

# ============================================================================ Rusk (encounter)
ENC_R = "enc_bridge_toll"
DEALT = "bandits_dealt_with"
PEACE_OUT = [SET("bandits_peaceful"), SET(DEALT), APP("seren", 8), APP("aldric", 5), APP("kael", 3),
             APP("morwen", -3), XP(300)]
RUSK_MAIN = [
    CHK("You hold that axe like a hay-fork. You're a farmer.", "Insight", 12, "r_ins_s", "r_ins_f", once=True),
    C("You're from Stoneford. You grow barley.", "r_story", [F("bandits_are_farmers")]),
    CHK("Whatever brought you here, robbing pilgrims won't fix it. Talk to me.", "Persuasion", 14, "r_story",
        "r_pers_f", once=True),
    CHK("Walk away. Now. While you still can.", "Intimidation", 15, "r_scared", "r_fight", once=True),
    C("Lay down your arms. Nobody needs to bleed on this bridge today.", "r_paladin", [CLS("Paladin")],
      tag="PALADIN"),
    C("(Plant your weapon in the planks.) One of us is leaving this bridge. Choose.", "r_scared", [CLS("Warrior")],
      tag="WARRIOR"),
    C("Five silver? Fine. Take it — and take your people out of the valley.", "r_paid", [GOLD(500)],
      out=[TG(500), SET("bandits_paid"), SET(DEALT)]),
    C("No toll. Stand aside.", "r_fight"),
]
WW.append(D("dlg_rusk", "r_hurt", [
    N("r_hurt", "rusk",
      "*A huge man in a patched coat blocks the Old Bridge, axe across his shoulders.* You cut down Tam and Wil. "
      "They were boys, with bows they couldn't string right. Give me one reason I shouldn't do the same to you.",
      [CHK("They drew on us. I'm sorry it came to that. It doesn't have to happen again.", "Persuasion", 17,
           "r_story", "r_fight", once=True),
       CHK("Because you'd lose.", "Intimidation", 13, "r_scared", "r_fight", once=True),
       C("Five silver, and you take your people out of the valley.", "r_paid", [GOLD(500)],
         out=[TG(500), SET("bandits_paid"), SET(DEALT)]),
       C("(Attack.)", "r_fight")],
      cond=[F("lookouts_done"), NF("lookouts_talked")], fallback="r1"),
    N("r1", "narrator",
      "*A huge man in a patched coat stands on the Old Bridge, axe across his shoulders. Behind him, a handful of "
      "thin, nervous figures clutch knives like they've never held one before.*", next="r2"),
    N("r2", "rusk",
      "Name's Rusk. This is my bridge now. Toll's five silver a head, or everything you've got, whichever's more. "
      "I'm told that's how it works.", RUSK_MAIN),
    N("r2b", "rusk", "*Rusk shifts his grip on the axe.* Well?", RUSK_MAIN),
    N("r_ins_s", "rusk",
      "*His jaw works.* Barley. Thirty years. Then the grey came up out of the ground, and the barley came up grey "
      "with it. Can't eat grey. Can't sell grey.", out=[XP(75)], next="r_story"),
    N("r_ins_f", "rusk", "I hold it how I like, Rusk growls, gripping it tighter.", next="r2b"),
    N("r_pers_f", "rusk", "*A short, tired laugh.* Talk. Everyone wants to talk. Talk doesn't grow barley.",
      next="r2b"),
    N("r_paladin", "narrator",
      "*Your voice carries, warm and certain, and every bandit on the bridge goes still. Rusk lowers the axe an "
      "inch.* 'You'd... hear us out?'", next="r_story"),
    N("r_story", "rusk",
      "The whole of Stoneford's gone grey. Fields, orchards, the lot. We came up the pilgrim road to beg at the "
      "shrine, and found the bridge empty and the shrine-folk gone. Took the bridge. Seemed easier than starving.",
      [C("Lanternvale needs hands. Sergeant Holt will find you honest work — and once the lanterns are lit, your "
         "fields will come back.", "r_peace"),
       C("That's a sad story. The toll still isn't happening.", "r_fight")]),
    N("r_peace", "rusk",
      "*He looks at his people. They look back at him, thin and hopeful.* ...Lanternvale. Honest work. Aye. "
      "Alright. If you're lying, I'll find you. If you're not... I'll owe you.", out=PEACE_OUT, next="r_peace2"),
    N("r_peace2", "narrator",
      "*The bandits file off the bridge toward the village. One of them, a girl of about fifteen, gives you a tiny "
      "wave.*", [C("(Continue.)")]),
    N("r_scared", "narrator",
      "*Something in your face makes Rusk's people take a step back, then another. Rusk swallows.* 'Right. Right. "
      "We're going. Out of the valley. You won't see us again.'",
      out=[SET("bandits_scared"), SET(DEALT), APP("morwen", 5), APP("seren", -5), XP(200)],
      choices=[C("(Continue.)")]),
    N("r_paid", "rusk",
      "*He stares at the coins, then at you.* You'd pay us to go. Not to pass — to GO. *He pockets them slowly.* "
      "There's an orchard two valleys over that's still green. We'll try there. Thank you, stranger.",
      out=[APP("seren", 3), APP("pip", -3), XP(150)], choices=[C("(Continue.)")]),
    N("r_fight", "rusk", "Right. The expensive way, then.", out=[APP("morwen", 2), FIGHT(ENC_R)]),
]))

# ============================================================================ Keeper Ishiro (encounter)
ENC_K = "enc_keeper"
K_DONE = "keeper_done"
SH.append(D("dlg_keeper_ishiro", "k1", [
    N("k1", "narrator",
      "*A tall, grey figure in a keeper's robes moves along a row of dead lanterns, pinching out flames that aren't "
      "there. Its face is a smudge of ash.*", next="k2"),
    N("k2", "keeper_ishiro",
      "Put them out... put them all out... if they are dark, he cannot drink them... *It turns.* You carry a flame. "
      "Give it to me. I will keep it dark. I will keep it safe.",
      [C("Keeper Ishiro. Your letter reached Maru. She knows. She's sorry it was late.", "k_letter",
         [F("ishiro_letter_read")]),
       C("Ishiro? It's Seren. Isolde's daughter.", "k_seren", [INP("seren")]),
       CHK("(Recite the keeper's evening prayer, carved on every lantern on the stair.)", "Religion", 15,
           "k_peace", "k_fail", once=True),
       CHK("You kept the lanterns for forty years. You can rest now. Let us finish your work.", "Persuasion", 16,
           "k_peace", "k_fail", once=True),
       C("(Lay a hand on his shoulder and lend him a little of your light.)", "k_peace", [CLS("Priest")],
         tag="PRIEST"),
       C("Stand aside, spirit.", "k_fight")]),
    N("k_letter", "narrator",
      "*The grey figure goes very still. Something like colour seeps back into the smudge of its face: an old man, "
      "tired and gentle.* 'Maru... read it? Then someone came. Someone finally came.'", out=[XP(200)],
      next="k_peace"),
    N("k_seren", "seren",
      "Keeper Ishiro? It's Seren. You gave me barley sweets when I was small, and told me lanterns were only stars "
      "that decided to stay.", next="k_seren2"),
    N("k_seren2", "keeper_ishiro",
      "*The grey figure tilts its head.* ...Little Seren. You came up the stair. Like her. *It sounds afraid.* Don't "
      "give it everything, child. Don't.", out=[APP("seren", 10)], next="k_peace"),
    N("k_fail", "narrator",
      "*The ash-face twists.* 'No. NO. You'll let him drink it!' *Every dead lantern in the hall flares grey.*",
      out=[FIGHT(ENC_K)]),
    N("k_fight", "narrator", "*The grey keeper raises its hands, and the air goes cold and colourless.*",
      out=[FIGHT(ENC_K)]),
    N("k_peace", "keeper_ishiro",
      "*He lowers his hands. For a moment he's only an old man in a faded robe.* The Warden is in the sanctum, "
      "drinking the Heart dry. He doesn't want to. He can't stop. Free him... and take these. Forty years of "
      "prayers. They'll know what to do.",
      out=[GIVE("ishiros_prayer_beads"), SET("ishiro_at_peace"), SET(K_DONE), APP("seren", 5), APP("torvan", 5),
           XP(400)], next="k_peace2"),
    N("k_peace2", "narrator",
      "*Keeper Ishiro bows, and comes apart gently into motes of grey that warm, slowly, to gold, and drift up the "
      "stair toward the sanctum.*", [C("(Continue.)")]),
]))

# ============================================================================ the Warden (encounter)
SH.append(D("dlg_warden_confront", "w1", [
    N("w1", "narrator",
      "*The sanctum is a ring of standing stones around a vast stone lantern — the Heart Lantern — its light a "
      "single dying ember. Curled around it, drinking that ember in long, shuddering breaths, is the Warden.*",
      next="w2"),
    N("w2", "narrator",
      "*He is enormous: a stag of mist and moss, his antlers hung with a hundred lanterns, every one burning grey. "
      "When he lifts his head, his eyes are empty as winter.*", next="w3"),
    N("w3", "hollow_warden",
      "...LITTLE... LIGHTS... *The voice is a hundred lanterns guttering at once.* SO... HUNGRY... SO... COLD... "
      "GIVE... ME... YOUR... FLAME...",
      [C("(Let Seren speak.)", "w_seren", [INP("seren")]),
       C("(Let Kael speak.)", "w_kael", [INP("kael")]),
       C("(Let Torvan speak.)", "w_torvan", [INP("torvan")]),
       CHK("Warden! Remember the Walk — every lantern in the valley, every autumn. Remember who you are!",
           "Religion", 14, "w_remember", "w_noremember", once=True),
       CHK("(Hold up the Kindling Taper and call to him as the herd calls to its stag.)", "Nature", 14,
           "w_remember", "w_noremember", once=True),
       C("Then come and take it.", "w_fight")]),
    N("w_seren", "seren",
      "I'm Isolde's daughter. I came to give you my light, the way she did. *Her voice shakes.* But I don't think "
      "that's what you need. I think you need to stop being hungry.", next="w_seren2"),
    N("w_seren2", "hollow_warden",
      "...ISOLDE... *For a heartbeat, the grey lanterns flicker gold.* ...RUN... LITTLE... ONE... I... CANNOT... "
      "STOP...", out=[APP("seren", 5), SET("warden_remembers")], next="w_fight"),
    N("w_kael", "kael",
      "Ten years ago you took her, and I let you. *He raises the greatsword.* I'm not letting you take anyone else.",
      out=[APP("kael", 5)], next="w_fight"),
    N("w_torvan", "torvan",
      "Great One. The Hornkin swore to guard your herd, and we failed you. *He lowers his horns.* Forgive what we "
      "must do.", out=[APP("torvan", 5)], next="w_fight"),
    N("w_remember", "narrator",
      "*The Warden shudders. For an instant a single antler-lantern burns true gold.* '...WALK... I... REMEMBER... "
      "WALKING...' *Then the grey swallows it again — but something in the great beast has loosened.*",
      out=[SET("warden_remembers"), XP(150)], next="w_fight"),
    N("w_noremember", "narrator",
      "*The Warden doesn't hear you. Or it hears, and the hunger is louder.*", next="w_fight"),
    N("w_fight", "narrator",
      "*The Warden rises, and rises, and keeps rising. Every lantern in its antlers gutters grey, and the sanctum "
      "goes very, very quiet.*", out=[FIGHT("enc_warden")]),
]))

# ============================================================================ the Warden's spirit
LIGHT_OUT = [SET("heart_lantern_lit"), SET("lanterns_rekindled"), SPECIAL("RekindleLanterns")]
SH.append(D("dlg_warden_spirit", "ws_after", [
    N("ws_after", "warden_spirit",
      "*The little stag of light dozes against the Heart Lantern, glowing like a banked hearth.* ...Thank you, "
      "little lights. Go home. Your valley is waiting for you.",
      [C("(Return to Lanternvale.)", out=[TP("lanternvale", "camphor")]), C("Rest well.")],
      cond=[F("heart_lantern_lit")], fallback="ws1"),
    N("ws1", "narrator",
      "*Where the Hollow Warden fell, mist settles... and from it, unsteady on new legs, rises a small stag of soft "
      "light, no bigger than a fawn. A single lantern hangs from one tiny antler, dark.*", next="ws2"),
    N("ws2", "warden_spirit",
      "...I was so hungry. I drank and drank and could not stop. *He looks at the Heart Lantern, barely an ember.* "
      "The Heart needs a new flame, or I will grow hungry again. Every ten years a summoner gives one. Every ten "
      "years, I take it.",
      [C("(Look to Seren.)", "ws_seren", [INP("seren")]),
       C("This taper carries a spark from every hearth in Lanternvale, and the embers the Hollow stole, set free. "
         "Would that be enough?", "ws_taper", [HAS("kindling_taper")]),
       C("A Kindling doesn't need one life. It needs light — and light can come from many hands.", "ws_wise",
         [F("knows_kindling_cost"), HAS("kindling_taper"), NINP("seren")]),
       C("Every traveller carries a little hearth-light. Take some of ours — a little from each of us, not all "
         "from one.", "ws_hands", [NHAS("kindling_taper")])]),
    N("ws_seren", "seren",
      "*Seren steps forward, staff chiming.* I'm ready. My mother gave you everything she had. I can— *Her voice "
      "catches.*",
      [C("Seren, wait. You don't have to give yourself. Use the taper — the whole valley's light, not one life.",
         "ws_seren_taper", [HAS("kindling_taper")], out=[APP("seren", 15), APP("kael", 10)]),
       CHK("(Look at her.) She's terrified, and she's going to do it anyway.", "Insight", 10, "ws_ins_s",
           "ws_ins_f", cond=[HAS("kindling_taper")]),
       C("If that's your choice, Seren, I'll honour it.", "ws_seren_sac", out=[APP("kael", -10), APP("lys", -10)])]),
    N("ws_ins_s", "narrator",
      "*Her knuckles are white on the staff. You put your hand over hers and gently lift the Kindling Taper between "
      "you.* 'Not alone,' you tell her. 'Not this time.'", out=[APP("seren", 15), APP("kael", 10), XP(100)],
      next="ws_seren_taper"),
    N("ws_ins_f", "narrator",
      "*You can't read her face in the glow. Instead you simply hold out the Kindling Taper.*", next="ws_seren_taper"),
    N("ws_seren_sac", "narrator",
      "*Seren lifts her staff, and her own light begins to rise from her like breath on a cold morning — until the "
      "little stag steps between her and the lantern.*", next="ws_seren_sac2"),
    N("ws_seren_sac2", "warden_spirit",
      "No. Your mother's gift taught me what such a flame costs. I will not take another. *He nudges the taper in "
      "your hand with his small nose.* That. Use that.", out=[APP("seren", 5)], next="ws_seren_taper"),
    N("ws_seren_taper", "seren",
      "*She looks at the taper, then at you, and laughs — a wet, startled laugh.* The whole valley's light. Not one "
      "life. Mother, why didn't anyone think of that? *She takes it in both hands.* Then let's kindle it together.",
      out=[SET("seren_kindled")], next="ws_light"),
    N("ws_wise", "warden_spirit",
      "...Many hands. *His small ears lift.* In a thousand years, no one ever offered me that. Show me.",
      out=[XP(100)], next="ws_taper"),
    N("ws_taper", "narrator",
      "*You raise the Kindling Taper. Its little flame — every hearth in Lanternvale, every freed ember — leans "
      "toward the Heart Lantern like a sunflower toward morning.*", next="ws_light"),
    N("ws_light", "narrator",
      "*The taper touches the Heart Lantern. For a moment, nothing. Then light — gold, warm, enormous — pours up "
      "through the sanctum, down the stair, across Whisperwood and into every lantern in the valley at once. Far "
      "below, a whole village gasps.*", out=[TAKE("kindling_taper")] + LIGHT_OUT + [XP(250)], next="ws_after2"),
    N("ws_hands", "narrator",
      "*One by one, you each cup your hands around the Heart Lantern's ember. A little warmth leaves each of you — "
      "no more than a deep breath's worth — and the ember catches. Gold light pours down the stair and into every "
      "lantern in the valley.*", out=LIGHT_OUT + [XP(250)], next="ws_after2"),
    N("ws_after2", "warden_spirit",
      "*The little stag shakes himself, and the lantern on his antler blooms gold.* ...Warm. I had forgotten warm. "
      "*He bows his small head.* Every autumn I will walk the valley again. Tell them to leave the lanterns out for "
      "me. And honey cakes.",
      [C("You remembered the Walk, even through the grey.", "ws_rem", [F("warden_remembers")]),
       C("(Return to Lanternvale.)", out=[TP("lanternvale", "camphor")]),
       C("Rest now, Warden.")]),
    N("ws_rem", "warden_spirit",
      "Because you reminded me. A light only needs reminding, sometimes. *He lies down against the Heart, content.*",
      [C("(Return to Lanternvale.)", out=[TP("lanternvale", "camphor")]), C("Rest now, Warden.")]),
]))


def build_ww():
    return {"_note": "Whisperwood dialogues. dlg_puddlecap, dlg_bandit_lookouts and dlg_rusk are encounter "
                     "dialogues: peaceful branches set the encounter's doneFlag (no combat); fight branches use "
                     "StartCombat with the encounter id.", "dialogues": WW}


def build_shrine():
    return {"_note": "Old Lantern Shrine dialogues. dlg_keeper_ishiro and dlg_warden_confront are encounter "
                     "dialogues; dlg_warden_spirit appears after the Warden is defeated.",
            "dialogues": SH}
