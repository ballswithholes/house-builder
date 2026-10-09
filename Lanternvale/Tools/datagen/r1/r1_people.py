"""The Hollow Heart's people, talk and quests.

  Bettany Quill (r1_quartermaster), the raid quartermaster by the stair: Into the Hollow Heart (clear the raid) and The
    Lights That Went Down (free Mirefen's drowned lights; at the heart lantern choose: home to the fen, or the grove).
    Both are offered behind Flag mq2_drowned_lanterns_done and Level 20. She sells raid consumables.
  Hinoki (r1_hinoki), the grove's last kodama: Seed on the Ash, the lore trail (the ash drift, the Twins' echo, the
    seed's husk) that names Vyrmathra and points to Ashwyrm's Roost. His hint also reveals the hidden offerings.
  The Twins' echo (after the Twins): rest them together (a tear) or ask Solace to stay (she heals the raid by the heart).
"""
from r1_map import F_THORNMAW, F_TWINS, F_MIRE, F_HEART

Q_CLEAR, Q_LIGHTS, Q_LORE = "r1_into_the_hollow", "r1_lights_that_went_down", "r1_seed_on_the_ash"
GATE = "mq2_drowned_lanterns_done"
MIN_LEVEL = 20


# ------------------------------------------------------------------------------------------------ dialogue helpers

def cond(t, key="", value="", amount=0):
    c = {"type": t}
    if key:
        c["key"] = key
    if value:
        c["value"] = value
    if amount:
        c["amount"] = amount
    return c


def flag(k): return cond("Flag", k)
def notflag(k): return cond("NotFlag", k)
def qstate(q, s): return cond("QuestState", q, s)
def qnot(q): return cond("QuestNotStarted", q)
def level(n): return cond("Level", amount=n)
def cls(c): return cond("Class", c)
def has(i, n=0): return cond("HasItem", i, amount=n)
def nothas(i): return cond("NotHasItem", i)


def out(t, key="", amount=0, value=""):
    o = {"type": t}
    if key:
        o["key"] = key
    if value:
        o["value"] = value
    if amount:
        o["amount"] = amount
    return o


def setflag(k): return out("SetFlag", k)
def start(q): return out("StartQuest", q)
def complete(q): return out("CompleteQuest", q)
def give(i, n=0): return out("GiveItem", i, n)
def take(i, n=0): return out("TakeItem", i, n)
def xp(n): return out("GiveXP", amount=n)
def fight(e): return out("StartCombat", e)


def C(text, nxt="", conds=None, outs=None, check=None, once=False, tag=""):
    c = {"text": text}
    if nxt:
        c["next"] = nxt
    if conds:
        c["conditions"] = conds
    if outs:
        c["outcomes"] = outs
    if check:
        c["check"] = check
    if once:
        c["once"] = True
    if tag:
        c["tag"] = tag
    return c


def chk(skill, dc, ok, ko): return {"skill": skill, "dc": dc, "success": ok, "failure": ko}


def N(nid, speaker, text, choices=None, conds=None, fallback="", outs=None, nxt=""):
    n = {"id": nid, "speaker": speaker, "text": text}
    if conds:
        n["conditions"] = conds
    if fallback:
        n["fallback"] = fallback
    if outs:
        n["outcomes"] = outs
    if choices:
        n["choices"] = choices
    if nxt:
        n["next"] = nxt
    return n


BYE = C("Goodbye.")
STEP = C("(Step back.)")
OFFER = [flag(GATE), level(MIN_LEVEL)]

# ------------------------------------------------------------------------------------------------ Quill

def dlg_quill():
    Q = "r1_quartermaster"
    return {"id": "dlg_r1_quartermaster", "start": "q_first", "nodes": [
        N("q_first", Q, "*A stout woman in a teal bandana looks up from a ledger with her pencil already pointing at you.* Ah! Customers. Or heroes. In my experience they're the same people with different amounts of mud on. Bettany Quill, quartermaster. If it goes down that hole, I've counted it first.",
          conds=[notflag("r1_met_quill")], fallback="q_hub", outs=[setflag("r1_met_quill")], nxt="q_hub"),
        N("q_hub", Q, "*Quill licks her pencil and squints at you over the ledger.* Right. What'll it be?", choices=[
            C("The Hollow Heart has stopped beating.", "q_clear_done", [qstate(Q_CLEAR, "report")],
              [setflag("r1_hollow_reported"), complete(Q_CLEAR)]),
            C("The drowned lights are free. I sent them home to the fen.", "q_lights_home",
              [qstate(Q_LIGHTS, "return"), flag("r1_lights_sent_home")],
              [setflag("r1_lights_reported"), complete(Q_LIGHTS), give("r1_fenlight_lantern")]),
            C("The drowned lights are free. They stayed to light the grove.", "q_lights_grove",
              [qstate(Q_LIGHTS, "return"), flag("r1_grove_relit")],
              [setflag("r1_lights_reported"), complete(Q_LIGHTS), give("r1_grovelight_charm")]),
            C("What's down there, exactly?", "q_offer_clear", OFFER + [qnot(Q_CLEAR)]),
            C("Granny Sen says the lights that went down are still down here.", "q_offer_lights", OFFER + [qnot(Q_LIGHTS)]),
            C("Who hired you to stand at the bottom of a hole?", "q_not_yet", [notflag(GATE)]),
            C("Any advice about what's waiting down there?", "q_advice", [flag(GATE)]),
            C("Let's see your supplies.", outs=[out("OpenVendor", Q)]),
            BYE]),
        N("q_offer_clear", Q, "*She turns the ledger round so you can read it. It is a list in neat columns: THORNS. TWINS (WEEPY). MOTHER (DO NOT). HEART.* Four of them between here and the bottom, and the last one beats. The archive in Brightwater is paying, the Reeve of Lowlantern is chipping in, mostly in fish. I need a raid's worth of heroes to go in and come out again. In that order.", choices=[
            C("We'll clear it. All four.", "q_accept_clear", OFFER + [qnot(Q_CLEAR)], [start(Q_CLEAR)]),
            C("Tell me about the four first.", "q_advice"),
            C("Not yet.")]),
        N("q_accept_clear", Q, "Lovely. I'll put you down in pencil. *She writes your name, then, after a moment's thought, draws a small heart beside it.* For luck. Bring me back the beat. Or at least the quiet where it used to be.", choices=[BYE]),
        N("q_offer_lights", Q, "*She goes quiet, which you suspect is rare.* She told me too. Every lantern that sank in the fen, its little light went down the undertow and fetched up here, caught in the thorns like moths in a web. Some are still in the roots on the way down. The rest are in the heart. Lowlantern would like them back. So would I. My gran's lantern went under, years ago.", choices=[
            C("I'll free every light I find.", "q_accept_lights", OFFER + [qnot(Q_LIGHTS)], [start(Q_LIGHTS)]),
            C("Not yet.")]),
        N("q_accept_lights", Q, "Mind how you do it. They're frightened, and the thorns bite. Sing to them, if you know how. Or cut slow. And whatever's left at the bottom, in the heart's old lantern... well. You'll know what to do when you see it.", choices=[BYE]),
        N("q_not_yet", Q, "*She taps the ledger.* The archive in Brightwater, the Reeve of Lowlantern and a frog who paid in pearls. But nobody goes down until Mirefen's drowned lanterns are seen to. The coven's still about up top, and I don't send heroes into a hole with hags at their backs. Sort out the fen, then come and see me.", choices=[
            C("Let's see your supplies.", outs=[out("OpenVendor", Q)]), BYE]),
        N("q_advice", Q, "*She counts on her fingers.* Thornmaw first: bites whoever's in front, throws thorns at everyone, and shakes out prickly little friends when it's hurt. Kill the friends. The Twins: Sorrow makes the whole room cry, and Solace mends her. Stop the mending. Mother Mire turns folk into frogs, boils the bog, and keeps more family in that pot than you'd credit. And the Heart doesn't move at all. It doesn't need to. It beats, and drinks the light, and grows things. Bring healers. Then bring more healers than that.", choices=[
            C("Back to business.", "q_hub"), BYE]),
        N("q_clear_done", Q, "*She draws a line under the last name in the ledger, very neatly.* Four of four. Do you know how rare that is? I've had heroes come back with a bucket of thorns and a story. You came back with the quiet. *She listens. Far below, nothing beats.* Here: the archive's money, the Reeve's fish money, and the pick of the old Heronguard stores. I was saving them for a special occasion.", choices=[BYE]),
        N("q_lights_home", Q, "Home? *She looks up the stair towards the fen, and her pencil stops.* Then tonight Lowlantern will see the lights come up out of the water, one by one, like they used to. My gran would have cried. I might. Not in front of you. Here. Bo Puddlefoot made this for whoever brought them home.", choices=[BYE]),
        N("q_lights_grove", Q, "To light the grove. *She chews her pencil.* Well. They've been down here longer than they were ever up there, I suppose, and the grove's been dark a long while. Lowlantern will understand. Hinoki will be beside himself. Here: the kodama left this with me, for whoever let the lights stay.", choices=[BYE]),
    ]}


# ------------------------------------------------------------------------------------------------ Hinoki

def dlg_hinoki():
    H = "r1_hinoki"
    return {"id": "dlg_r1_hinoki", "start": "h_first", "nodes": [
        N("h_first", H, "*A little white spirit sits on the edge of the altar, swinging its feet. Its head rattles when it turns, very slowly.* Oh. You can see me. Most people only see the dark now. *rattle* I am Hinoki. I was the smallest kodama of this grove. Now I am the last one.",
          conds=[notflag("r1_met_hinoki")], fallback="h_hub", outs=[setflag("r1_met_hinoki")], nxt="h_hub"),
        N("h_hub", H, "*Hinoki rattles, politely.* Yes?", choices=[
            C("I found the husk of the seed.", "h_done", [qstate(Q_LORE, "return"), has("r1_hollow_seed_husk")],
              [take("r1_hollow_seed_husk"), setflag("r1_seed_reported"), complete(Q_LORE), setflag("r1_seed_lore_known")]),
            C("What happened to this grove?", "h_offer", OFFER + [qnot(Q_LORE)]),
            C("Is anything else hidden down here?", "h_offerings", [notflag("r1_found_offerings")]),
            C("The lights stayed. The grove is lit again.", "h_grove", [flag("r1_grove_relit"), notflag("r1_hinoki_thanked")],
              [setflag("r1_hinoki_thanked")]),
            C("The lights have gone home to the fen.", "h_home", [flag("r1_lights_sent_home"), notflag("r1_hinoki_thanked")],
              [setflag("r1_hinoki_thanked")]),
            C("Solace stayed. She's waiting by the heart.", "h_solace", [flag("r1_solace_stays"), notflag("r1_hinoki_solace")],
              [setflag("r1_hinoki_solace")]),
            C("What was the grove like, before?", "h_before"),
            BYE]),
        N("h_offer", H, "*Rattle. Rattle.* It began with grey snow. Twelve winters ago it fell all over the south, soft and warm, which snow should never be. That was the year your heron-knights rode north, forty of them, under a sky like a burnt pan. Some of the grey snow ran down the springs into our roots, and in it there was a seed. *He looks at his small hands.* We thought it was a gift. It drank a little light, and grew a little. Then it drank more.", choices=[
            C("I'll find out where the seed came from.", "h_accept", OFFER + [qnot(Q_LORE)], [start(Q_LORE)]),
            C("Not yet.")]),
        N("h_accept", H, "Three things, then. There is ash still lying in the Root Gallery, where the grey roots begin: look at it with clever eyes. The twins who guarded us remember the snow; if you free them, they may tell you. And when the heart stops... *rattle* ...the seed will leave its husk behind. Bring it to me. I want to know whose seed it is.", choices=[BYE]),
        N("h_offerings", H, "*He leans close and whispers, though there is nobody to hear.* When the grey came, my sisters hid the grove's offerings in the corner where the pools meet the north wall, behind a veil of thorns. Candles, and coins, and good luck. Go and look past the first pool. They would want somebody kind to have them.",
          outs=[setflag("r1_found_offerings")], choices=[BYE]),
        N("h_before", H, "There was a lantern in every root, and the twins sang them lit at dusk, and Thornmaw dug springs with its nose and let us ride on its back. Mother Mire came down to trade sometimes. She was always rude. She was not always wicked.", choices=[
            C("Back to the present, then.", "h_hub"), BYE]),
        N("h_done", H, "*He takes the husk in both hands. It is grey and light and smells faintly of smoke.* Dragon. *Rattle.* This was never the seed of any tree. It is a cinder-seed: one of the embers a great wyrm breathes out in her sleep. Vyrmathra, the old songs call her, the Ashwyrm under Skyreach. She dreams, and when she dreams the ash flies south, and where it lands things grow hungry. *He holds the husk close.* There is a roost above the peaks where her servants keep the fire. If you go there, go with many friends. More than you brought here.", choices=[BYE]),
        N("h_grove", H, "*He looks round at the lit lanterns and says nothing for a long time. Then he rattles, very fast, which you suspect is how kodama cry.* Thank you. Thank you. I will look after them. I am the smallest, but I am very good at looking after things.", choices=[BYE]),
        N("h_home", H, "*He looks up the stair, towards the fen.* Home. Yes. They were never ours to keep; we only borrowed their light. *rattle* It will be dark here a while longer. That is all right. Dark is only the part before the morning.", choices=[BYE]),
        N("h_solace", H, "Solace. *His rattle goes soft.* She always stayed up last, to blow out the lanterns. Now she will stay up to light them. I will go and sit with her.", choices=[BYE]),
    ]}


# ------------------------------------------------------------------------------------------------ props

LIGHT_TEXT = {
    1: "*A paper lantern, drowned-dark, is caught in a knot of black thorns by the gallery's edge. Deep inside it a tiny light beats against the paper like a moth.*",
    2: "*Behind Thornmaw's den, a lantern hangs in the thorns with its paper torn. The light inside has crept into the last whole corner and curled up there.*",
    3: "*At the edge of the pools a lantern bobs in the thorns, half in the water. Its light is so faint you only see it when you stop looking.*",
    4: "*In the reeds of Mire Hollow, a lantern has been knotted into the thorns with a coven cord. The light inside flickers at you, hopeful and afraid.*",
}


def dlg_captive_light(n):
    q, f = Q_LIGHTS, f"r1_light_{n}"
    rescued = lambda nid, text, amount: N(nid, "narrator", text, outs=[setflag(f), xp(amount)], choices=[STEP])
    return {"id": f"dlg_r1_captive_light_{n}", "start": "c_free", "nodes": [
        N("c_free", "narrator", LIGHT_TEXT[n], conds=[qstate(q, "lights"), notflag(f)], fallback="c_idle", choices=[
            C("Sing the sending that Granny Sen sang for Mirefen.", check=chk("Religion", 14, "c_sung", "c_fail"), once=True, tag="RELIGION"),
            C("Coax the thorns to loosen, one at a time.", check=chk("Nature", 14, "c_coaxed", "c_fail"), once=True, tag="NATURE"),
            C("Tear the thorns apart with your bare hands.", check=chk("Athletics", 13, "c_torn", "c_fail"), once=True, tag="ATHLETICS"),
            C("Pray for the lost light.", "c_prayed", [cls("Priest")], tag="PRIEST"),
            C("Pray for the lost light.", "c_prayed", [cls("Paladin")], tag="PALADIN"),
            C("Ask the roots to let go.", "c_roots", [cls("Shaman")], tag="SHAMAN"),
            C("Unpick the blight's weave.", "c_weave", [cls("Mage")], tag="MAGE"),
            C("Unpick the blight's weave.", "c_weave", [cls("Warlock")], tag="WARLOCK"),
            C("(Cut the thorns slowly, one by one.)", "c_slow"),
            STEP]),
        rescued("c_sung", "You sing the old fen sending under your breath, the one about the long road home. The thorns don't loosen, but the light does: it slips out through a tear in the paper, warms your hands a moment, and settles in a lantern that isn't drowned any more.", 120),
        rescued("c_coaxed", "Thorn by thorn the bramble gives way, the way brambles do for people who are patient with them. The lantern comes free, and its light brightens until the paper glows.", 120),
        rescued("c_torn", "The thorns bite, and you bite back. The bramble tears apart in your hands, and the light inside flares up so brightly you have to squint.", 120),
        rescued("c_prayed", "You say the words for the lost and the late. The thorns go grey and crumble, and the light rises into a whole lantern, glowing steady.", 120),
        rescued("c_roots", "You put a hand on the root and ask. It considers this for a long, slow moment, then unwinds, quite politely. The light rises free.", 120),
        rescued("c_weave", "There is a pattern in the blight, knotted tight around the lantern. You find the loose end and pull. The whole weave unravels, and the light pours out like warm honey.", 120),
        rescued("c_slow", "It takes a long time, and the thorns get you more than once. But the last one comes away at last, and the light inside drifts up into a whole, warm lantern.", 60),
        N("c_fail", "narrator", "The thorns tighten round the paper with a dry rasp, and the little light flinches back. Not like that.", choices=[
            C("(Cut the thorns slowly, one by one.)", "c_slow"), STEP]),
        N("c_idle", "narrator", "A drowned paper lantern, caught in black thorns. Deep inside, a tiny light beats against the paper like a moth. Somebody at the camp by the stair might know what to do for it.",
          conds=[notflag(f)], fallback="c_lit", choices=[STEP]),
        N("c_lit", "narrator", "The lantern glows warmly now, as if it had never been anywhere dark.", choices=[STEP]),
    ]}


def dlg_ash_drift():
    q = Q_LORE
    found = lambda nid, text, amount: N(nid, "narrator", text, outs=[setflag("r1_lore_ash")] + ([xp(amount)] if amount else []), choices=[STEP])
    return {"id": "dlg_r1_ash_drift", "start": "a_read", "nodes": [
        N("a_read", "narrator", "*Here the great roots have gone grey to the tips. In the crook of one lies a drift of fine ash, dry as an old book though water drips all round it. Something in the drift glints, like a coal that forgot to go out.*",
          conds=[qstate(q, "ash")], fallback="a_plain", choices=[
            C("Read the glint.", check=chk("Arcana", 13, "a_arcana", "a_dull"), once=True, tag="ARCANA"),
            C("This isn't fen ash. Where did it fall from?", check=chk("Nature", 13, "a_nature", "a_dull"), once=True, tag="NATURE"),
            C("Grey snow... there was a winter like this once.", check=chk("History", 14, "a_history", "a_dull"), once=True, tag="HISTORY"),
            C("Taste the magic on it.", "a_arcana", [cls("Mage")], tag="MAGE"),
            C("Ask the ash where it was born.", "a_nature", [cls("Shaman")], tag="SHAMAN"),
            C("(Scoop a little into a pouch for Hinoki.)", "a_scoop"),
            STEP]),
        found("a_arcana", "Fire-magic, old and enormous, folded so small it fits inside a cinder. Not a mage's spell: a breath. Something vast breathed this out in its sleep, very far away, and it is still warm.", 100),
        found("a_nature", "Mountain ash, glassy and sharp, the kind that falls from a smoking peak. It came a long way south on a high wind, fell in a storm, and ran down the springs. The roots drank it, and went grey.", 100),
        found("a_history", "Twelve winters ago, grey snow fell over the south for three days, warm to the touch. The farmers called it a blessing, because the barley came up early. It was the winter the Heronguard rode north to Skyreach: forty knights. Eleven came home.", 100),
        found("a_dull", "The ash is just ash, as far as you can tell: grey, dry and old. You scoop a little into a pouch anyway. Hinoki will know more.", 0),
        found("a_scoop", "You scoop a pinch of the ash into a pouch. It is warm. Ash should not be warm.", 0),
        N("a_plain", "narrator", "A drift of grey ash lies in the roots, dry and faintly warm. It does not belong down here.", choices=[STEP]),
    ]}


def dlg_seed_husk():
    return {"id": "dlg_r1_seed_husk", "start": "s_take", "nodes": [
        N("s_take", "narrator", "*Where the heart hung, a grey shell lies in the roots: a husk the size of two fists, split open, smelling faintly of smoke. Whatever grew out of it is gone.*",
          conds=[nothas("r1_hollow_seed_husk")], fallback="s_have", choices=[
            C("(Take the husk.)", "s_taken", outs=[give("r1_hollow_seed_husk"), setflag("r1_husk_taken")]), STEP]),
        N("s_taken", "narrator", "It is lighter than it looks, and warm. Hinoki will want to see it.", choices=[STEP]),
        N("s_have", "narrator", "You already carry the seed's husk.", choices=[STEP]),
    ]}


def dlg_heart_lantern():
    return {"id": "dlg_r1_heart_lantern", "start": "hl_free", "nodes": [
        N("hl_free", "narrator", "*The old heart lantern of the grove sits tilted in the roots, its fire-box cracked open. Inside, dozens of small lights crowd together: every lantern that ever drowned in Mirefen. They turn towards you, the way lights do.*",
          conds=[flag(F_HEART), qstate(Q_LIGHTS, "release")], fallback="hl_quiet", choices=[
            C("Go home. The fen is waiting for you.", "hl_home", outs=[setflag("r1_lights_sent_home"), setflag("r1_lights_released")]),
            C("Stay, and light the grove that kept you.", "hl_grove", outs=[setflag("r1_grove_relit"), setflag("r1_lights_released")]),
            C("(Step back and think about it.)")]),
        N("hl_home", "narrator", "The lights rise out of the lantern in a long, slow ribbon and drift away up the chamber, past the pools, up the stair, towards the fen. Somewhere far above, in Lowlantern, someone is going to look out at the water tonight and gasp.", choices=[STEP]),
        N("hl_grove", "narrator", "The lights spill out of the lantern and go looking for homes. One by one the drowned lanterns of the grove catch, and glow, and the whole cavern turns the colour of evening. It has been dark down here for twelve years. It isn't any more.", choices=[STEP]),
        N("hl_quiet", "narrator", "The old heart lantern is quiet and warm. Whatever was caught in it is gone, one way or another.", conds=[flag(F_HEART)], fallback="hl_beating", choices=[STEP]),
        N("hl_beating", "narrator", "*The heart beats in its cage of roots, and the old lantern behind it glows rose with every beat. You cannot get near it while the heart still beats.*", choices=[STEP]),
    ]}


# ------------------------------------------------------------------------------------------------ bosses and the echo

def dlg_bosses():
    return [
        {"id": "dlg_r1_thornmaw", "start": "t_wake", "nodes": [
            N("t_wake", "narrator", "*The roots ahead are not roots. They breathe. Something the size of a cottage uncurls in the den, all bark and bramble, and four violet eyes open one after another. Deep in its chest, through the thorns, something grey glows like a coal.*", choices=[
                C("(Draw your weapons.)", outs=[fight("enc_r1_thornmaw")]),
                C("It was a spring-digger once. Speak to it gently.", check=chk("Nature", 15, "t_gentle", "t_snarl"), once=True, tag="NATURE"),
                C("(Back away slowly.)")]),
            N("t_gentle", "narrator", "For a heartbeat the four eyes soften, and the great head dips, the way a dog's does when it hears its name. Then the grey coal in its chest flares, and the thorns stand up again. Whatever it was is still in there. It just can't get out.", choices=[
                C("(Draw your weapons. Free it.)", outs=[fight("enc_r1_thornmaw")]), C("(Back away slowly.)")]),
            N("t_snarl", "narrator", "Thornmaw snarls, and the whole den snarls with it.", choices=[
                C("(Draw your weapons.)", outs=[fight("enc_r1_thornmaw")]), C("(Back away slowly.)")]),
        ]},
        {"id": "dlg_r1_twins", "start": "w_1", "nodes": [
            N("w_1", "r1_sorrow", "*Two figures float above the water, one moon-blue, one dawn-rose, and both are weeping.* Another light... come down to drown with the others? Shh. Shh. Let us weep for you first.", nxt="w_2"),
            N("w_2", "r1_solace", "Sister, they are not drowned. Not yet. *She looks at you, and her tears run faster.* Oh, little lights. I am sorry. We cannot stop. The heart will not let us stop.", choices=[
                C("Then we'll stop you. Gently, if we can.", outs=[fight("enc_r1_twins")]),
                C("What does the heart want with you?", "w_3"),
                C("(Back away.)")]),
            N("w_3", "r1_sorrow", "What it always wants. More. *Her veil lifts.* It drank the lanterns, and then the springs, and then us. Come closer. You are so very bright.", choices=[
                C("(Raise your weapons.)", outs=[fight("enc_r1_twins")]), C("(Back away.)")]),
        ]},
        {"id": "dlg_r1_mother_mire", "start": "m_1", "nodes": [
            N("m_1", "r1_mother_mire_voice", "Well, well, well. *A huge hunched shape straightens over the cauldron, and keeps on straightening.* Gall's little hag-hunters! You cut my cords and broke my daughters' totems, and now you've come all the way down to Mother's kitchen with mud on your boots.", choices=[
                C("Your coven drowned a whole fen's lights. It ends here.", outs=[fight("enc_r1_mother_mire")]),
                C("What did the heart promise you?", "m_2"),
                C("(Back away.)")]),
            N("m_2", "r1_mother_mire_voice", "Promise? *She cackles, and something in the cauldron cackles with her.* A seed doesn't promise, dearie. It grows. And when it's grown big enough, the one who dropped it will come south to see her garden: great and grey and warm as a hearth. And Mother will be first in line to curtsey.", choices=[
                C("Then you'll be curtseying alone.", outs=[fight("enc_r1_mother_mire")]), C("(Back away.)")]),
        ]},
        {"id": "dlg_r1_hollow_heart", "start": "hh_1", "nodes": [
            N("hh_1", "narrator", "*BOOM. ...boom. The old heart lantern hangs in its cage of roots, and in it beats something rose-violet and veined and vast. When it speaks, it speaks with every drowned light at once, and none of the voices are its own.*", nxt="hh_2"),
            N("hh_2", "r1_heart_voice", "...light... *boom* ...more light... the fire... in the north... is hungry... too...", choices=[
                C("(End it.)", outs=[fight("enc_r1_hollow_heart")]), C("(Back away.)")]),
        ]},
        {"id": "dlg_r1_twin_echo", "start": "e_1", "nodes": [
            N("e_1", "r1_twin_echo", "*Where the twins fell, a single figure kneels at the water's edge: half moon-blue, half dawn-rose, as though two people were trying very hard to be one.* You stopped us. Thank you. We were so tired of weeping.", choices=[
                C("Who were you, before?", "e_who"), C("What happened here?", "e_story"), BYE]),
            N("e_who", "r1_twin_echo", "The grove's keepers. I kept its grief, so nobody else had to carry it. I kept its comfort, so there was always enough to go round. We sang the lanterns lit at dusk. *Both halves smile at once.* We were very good at it.", choices=[
                C("What happened here?", "e_story"), BYE]),
            N("e_story", "r1_twin_echo", "Grey snow, twelve winters ago, warm as a hearth. It fell for three days, and far off we saw riders in heron-white going north under it. Then a seed came down the springs. The lanterns began to go dark, and Mother Mire came, and smiled, and fed it. *The figure looks at the pools.* It took the lights first. Then it took us.", choices=[
                C("Your grief is spent. Lie down together now, both of you.", "e_rest", outs=[setflag("r1_lore_twins"), setflag("r1_twins_rested"), give("r1_twinned_tear")]),
                C("Solace, the grove will need comfort when this is over. Will you stay?", "e_stay", outs=[setflag("r1_lore_twins"), setflag("r1_solace_stays")]),
                C("Not yet.")]),
            N("e_rest", "r1_twin_echo", "*The two halves lean together until there is only one light, and then not even that. Something small and cold is left on the stone where they knelt: a single tear, frozen into a bead.*", choices=[C("(Keep the tear.)")]),
            N("e_stay", "r1_twin_echo", "*The moon-blue half fades with a sigh, like a held breath let go. The dawn-rose half stands up, and is only Solace, and she is not weeping.* I will stay. I will wait by the heart until it is quiet, and then I will light the lanterns again. When you are tired, come and sit in my light.", choices=[BYE]),
        ]},
        {"id": "dlg_r1_solace", "start": "so_1", "nodes": [
            N("so_1", "r1_solace_light", "*Solace sits on a root with her small lantern in her lap.* Sit a moment. I will hold the light.", choices=[
                C("(Sit a while in her light.)", "so_2", outs=[out("HealParty")]), BYE]),
            N("so_2", "r1_solace_light", "*Warmth, like a blanket fresh off the line. Every ache goes quiet.* There. Now go and be brave somewhere else for a while.", choices=[BYE]),
        ]},
    ]


def dialogues():
    return [dlg_quill(), dlg_hinoki()] + [dlg_captive_light(n) for n in range(1, 5)] + \
        [dlg_ash_drift(), dlg_seed_husk(), dlg_heart_lantern()] + dlg_bosses()


# ------------------------------------------------------------------------------------------------ npcs

def npcs():
    return [
        {"id": "r1_quartermaster", "name": "Bettany Quill", "shortName": "Quill", "title": "Raid Quartermaster",
         "sprite": "npc_quartermaster", "portrait": "portrait_merchant", "dialogue": "dlg_r1_quartermaster",
         "vendor": [{"item": i} for i in ["r1_heartwood_draught", "r1_glowcap_tonic", "r1_thornbark_elixir", "r1_lanternlight_elixir",
                                           "potion_healing", "potion_lesser_mana", "elixir_lantern_oil", "bandage_wool"]],
         "bark": "Pencil, ledger, potions. In that order."},
        {"id": "r1_hinoki", "name": "Hinoki", "title": "The Last Kodama", "sprite": "npc_spirit", "portrait": "portrait_spirit",
         "dialogue": "dlg_r1_hinoki", "bark": "*rattle* ...hello."},
        {"id": "r1_twin_echo", "name": "Echo of the Twins", "shortName": "Echo", "sprite": "cr_r1_twin_solace",
         "portrait": "cr_hollow_spirit", "dialogue": "dlg_r1_twin_echo", "bark": "*A soft sound, half a sigh and half a song.*"},
        {"id": "r1_solace_light", "name": "Solace", "title": "Keeper of the Grove's Comfort", "sprite": "cr_r1_twin_solace",
         "portrait": "cr_hollow_spirit", "dialogue": "dlg_r1_solace", "bark": "Sit a moment. I will hold the light."},
        # speakers of the boss encounters (never placed)
        {"id": "r1_sorrow", "name": "Sorrow", "sprite": "cr_r1_twin_sorrow", "portrait": "cr_hollow_spirit"},
        {"id": "r1_solace", "name": "Solace", "sprite": "cr_r1_twin_solace", "portrait": "cr_hollow_spirit"},
        {"id": "r1_mother_mire_voice", "name": "Mother Mire", "sprite": "cr_r1_mother_mire", "portrait": "cr_hollow_treant"},
        {"id": "r1_heart_voice", "name": "The Hollow Heart", "sprite": "cr_r1_hollow_heart", "portrait": "cr_hollow_wisp"},
    ]


# ------------------------------------------------------------------------------------------------ quests

def obj(t, target, text, count=1):
    o = {"type": t, "target": target, "text": text}
    if count != 1:
        o["count"] = count
    return o


def stage(sid, desc, objectives, nxt="", turn_in="", on=None):
    s = {"id": sid, "description": desc, "objectives": objectives, "next": nxt}
    if on:
        s["onComplete"] = on
    if turn_in:
        s["turnIn"] = turn_in
    return s


QUEST_XP = {Q_CLEAR: 700, Q_LIGHTS: 350, Q_LORE: 450}


def quests():
    Z = "raid_hollow_heart"
    return [
        {"id": Q_CLEAR, "name": "Into the Hollow Heart", "giver": "r1_quartermaster", "level": 22, "minLevel": MIN_LEVEL, "zone": Z,
         "summary": "Bettany Quill keeps the ledger at the top of the Hollow Heart's stair. Four things stand between her and a quiet grove: Thornmaw, the Weeping Twins, Mother Mire and the Hollow Heart itself.",
         "stages": [
             stage("thornmaw", "Thornmaw the Rootbound guards the way down from its den of thorns.", [obj("Defeat", "enc_r1_thornmaw", "Thornmaw the Rootbound defeated")], "twins"),
             stage("twins", "The black thorns have withered. Beyond them, the Weeping Twins drift between the pools.", [obj("Defeat", "enc_r1_twins", "The Weeping Twins at rest")], "mire"),
             stage("mire", "Mother Mire keeps her kitchen in Mire Hollow, past the thorns the Twins held shut.", [obj("Defeat", "enc_r1_mother_mire", "Mother Mire defeated")], "heart"),
             stage("heart", "The way to the Heart Chamber is open. Still the Hollow Heart.", [obj("Defeat", "enc_r1_hollow_heart", "The Hollow Heart stilled")], "report"),
             stage("report", "Tell Quill the heart has stopped beating.", [obj("Flag", "r1_hollow_reported", "Report to Bettany Quill")], "", "r1_quartermaster"),
         ],
         "rewards": {"xp": QUEST_XP[Q_CLEAR], "gold": 30000,
                     "choiceItems": ["r1_cloak_of_the_stair", "r1_kodama_bell", "r1_banked_ember_ring", "r1_rootsplitter_band"]}},
        {"id": Q_LIGHTS, "name": "The Lights That Went Down", "giver": "r1_quartermaster", "level": 22, "minLevel": MIN_LEVEL, "zone": Z,
         "summary": "Every lantern that drowned in Mirefen sent its light down the undertow into the Hollow Heart. Free the lights caught in the thorns, then decide what becomes of the ones the heart swallowed.",
         "stages": [
             stage("lights", "Four drowned lights are caught in the thorns: in the Root Gallery, behind Thornmaw's den, at the edge of the Weeping Pools and in the reeds of Mire Hollow.",
                   [obj("Flag", "r1_light_1", "The light in the Root Gallery"), obj("Flag", "r1_light_2", "The light behind the den"),
                    obj("Flag", "r1_light_3", "The light by the pools"), obj("Flag", "r1_light_4", "The light in the reeds")], "release"),
             stage("release", "The rest of the lights are in the heart's old lantern. When the Hollow Heart is still, go to the lantern and set them free: home to the fen, or to light the grove.",
                   [obj("Flag", "r1_lights_released", "The lights in the heart lantern set free")], "return"),
             stage("return", "Tell Quill what became of the lights.", [obj("Flag", "r1_lights_reported", "Report to Bettany Quill")], "", "r1_quartermaster"),
         ],
         "rewards": {"xp": QUEST_XP[Q_LIGHTS], "gold": 15000}},
        {"id": Q_LORE, "name": "Seed on the Ash", "giver": "r1_hinoki", "level": 22, "minLevel": MIN_LEVEL, "zone": Z,
         "summary": "Hinoki, the grove's last kodama, wants to know where the Hollow seed came from. It fell with grey snow twelve winters ago, the year the Heronguard rode north.",
         "stages": [
             stage("ash", "Look at the ash drift where the grey roots begin, in the Root Gallery.", [obj("Flag", "r1_lore_ash", "The ash in the Root Gallery examined")], "twins"),
             stage("twins", "The Weeping Twins remember the grey snow. Free them, and hear what their echo has to say.", [obj("Flag", "r1_lore_twins", "Hear the Twins' echo")], "husk", "r1_twin_echo"),
             stage("husk", "When the Hollow Heart stops, the seed will leave its husk behind in the roots. Take it.", [obj("Collect", "r1_hollow_seed_husk", "The husk of the Hollow seed")], "return"),
             stage("return", "Bring the husk to Hinoki.", [obj("Flag", "r1_seed_reported", "Show Hinoki the husk")], "", "r1_hinoki"),
         ],
         "rewards": {"xp": QUEST_XP[Q_LORE], "gold": 12000, "choiceItems": ["r1_hinokis_rattle", "r1_ashglass_loupe"]}},
    ]


# ------------------------------------------------------------------------------------------------ items

def epic(iid, name, icon, desc, equip, stats, ilvl=28, kind="Accessory", armor=0, armor_type=""):
    d = {"id": iid, "name": name, "icon": icon, "description": desc, "kind": kind, "quality": "Epic", "itemLevel": ilvl,
         "requiredLevel": 20, "equip": equip}
    if armor_type:
        d["armorType"] = armor_type
    if armor:
        d["armor"] = armor
    d["stats"] = [{"stat": s, "value": v} for s, v in stats]
    d["price"] = 14000 + (ilvl - 26) * 1200
    return d


def items():
    return [
        # consumables (Quill's stall)
        {"id": "r1_heartwood_draught", "name": "Heartwood Draught", "icon": "potion_red",
         "description": "Brewed from the sap of a grove that remembers being green. Tastes of rain and a little of cinnamon.",
         "kind": "Consumable", "quality": "Common", "itemLevel": 30, "requiredLevel": 20, "use": "r1_use_heartwood_draught",
         "consumable": True, "stack": 5, "price": 900},
        {"id": "r1_glowcap_tonic", "name": "Glowcap Tonic", "icon": "potion_blue",
         "description": "Glowcap mushrooms steeped in spring water. It glows in the dark, and so, briefly, do you.",
         "kind": "Consumable", "quality": "Common", "itemLevel": 30, "requiredLevel": 20, "use": "r1_use_glowcap_tonic",
         "consumable": True, "stack": 5, "price": 800},
        {"id": "r1_thornbark_elixir", "name": "Thornbark Elixir", "icon": "vial",
         "description": "Bitter as bark, because it is mostly bark. Thorns and bog-brews slide off you like rain off a roof.",
         "kind": "Consumable", "quality": "Common", "itemLevel": 26, "requiredLevel": 20, "use": "r1_use_thornbark_elixir",
         "consumable": True, "stack": 5, "price": 600},
        {"id": "r1_lanternlight_elixir", "name": "Lanternlight Elixir", "icon": "fire",
         "description": "Quill's own recipe: lamp oil, honey, and a candle-end steeped overnight. Keeps the dark from getting in.",
         "kind": "Consumable", "quality": "Common", "itemLevel": 26, "requiredLevel": 20, "use": "r1_use_lanternlight_elixir",
         "consumable": True, "stack": 5, "price": 600},
        # the quest item
        {"id": "r1_hollow_seed_husk", "name": "Husk of the Hollow Seed", "icon": "leaf",
         "description": "A grey shell the size of two fists, split open and smelling faintly of smoke. Warm, still.",
         "kind": "Quest", "quality": "Common", "itemLevel": 22, "stack": 1, "price": 0, "unique": True, "quest": Q_LORE},
        # rewards (Epic tier, required level 20)
        epic("r1_cloak_of_the_stair", "Cloak of the Lantern Stair", "wings",
             "Heronguard wool, dyed the blue of a lantern's heart. Quill swears it has never once been rained on.",
             "Back", [("Stamina", 9), ("Strength", 5), ("Defense", 5)], kind="Armor", armor=27, armor_type="Cloth"),
        epic("r1_kodama_bell", "Kodama Bell Necklace", "hands_pray",
             "A tiny wooden bell on a cord. It rattles when someone near you is hurt, quite insistently.",
             "Neck", [("Intellect", 5), ("Spirit", 5), ("Stamina", 3), ("HealingPower", 15)]),
        epic("r1_banked_ember_ring", "Ring of the Banked Ember", "ember",
             "A ring with a coal set in it, banked low for the night. Breathe on it and it remembers it is fire.",
             "Finger", [("Intellect", 5), ("Stamina", 4), ("SpellDamage", 10)]),
        epic("r1_rootsplitter_band", "Rootsplitter Band", "fist",
             "A band of black iron worn smooth by a root-cutter's grip. It still wants to cut something.",
             "Finger", [("Strength", 6), ("Agility", 5), ("Stamina", 5), ("AttackPower", 8)]),
        epic("r1_fenlight_lantern", "Fenlight Lantern", "ember",
             "A little lantern on a cord, made by Bo Puddlefoot from a jar, a wick and a lot of gratitude.",
             "Trinket", [("Spirit", 6), ("Stamina", 5), ("HealingPower", 14)], ilvl=27),
        epic("r1_grovelight_charm", "Grovelight Charm", "leaf",
             "A paper charm folded by a kodama. It smells of moss and of a lantern freshly lit.",
             "Trinket", [("Strength", 5), ("Agility", 5), ("Stamina", 6)], ilvl=27),
        epic("r1_twinned_tear", "The Twinned Tear", "water_drop",
             "A single frozen tear on a silver thread. It is cold to touch, and somehow comforting.",
             "Neck", [("Intellect", 5), ("Stamina", 5), ("Spirit", 4), ("SpellDamage", 6)], ilvl=27),
        epic("r1_hinokis_rattle", "Hinoki's Rattle", "star",
             "The little wooden rattle of a kodama's head, given freely. It rattles when you are brave.",
             "Trinket", [("Stamina", 6), ("Spirit", 6), ("Intellect", 4)], ilvl=27),
        epic("r1_ashglass_loupe", "Ashglass Loupe", "eye",
             "A lens ground from the ash-glass of the grey snow. Through it, everything far away looks a little nearer.",
             "Trinket", [("Agility", 5), ("Stamina", 5), ("AttackPower", 12)], ilvl=27),
    ]


def item_abilities():
    return [
        {"id": "r1_use_heartwood_draught", "name": "Heartwood Draught", "icon": "potion_red", "description": "Restores {0} health.",
         "school": "Physical", "time": "OffGcd", "target": "Self", "hidden": True, "breaksStealth": True, "cooldown": 120,
         "cooldownGroup": "potion", "effects": [{"type": "Heal", "min": 640, "max": 820, "target": "Self"}], "aiHint": "Heal"},
        {"id": "r1_use_glowcap_tonic", "name": "Glowcap Tonic", "icon": "potion_blue", "description": "Restores {0} mana.",
         "school": "Physical", "time": "OffGcd", "target": "Self", "hidden": True, "breaksStealth": True, "cooldown": 120,
         "cooldownGroup": "potion", "effects": [{"type": "GainResource", "resource": "Mana", "amount": 720, "target": "Self"}], "aiHint": "Buff"},
        {"id": "r1_use_thornbark_elixir", "name": "Thornbark Elixir", "icon": "vial",
         "description": "Nature resistance increased by 25 and armor by 150 for 1 hour.", "school": "Physical", "time": "Gcd",
         "target": "Self", "hidden": True, "breaksStealth": True, "effects": [{"type": "ApplyAura", "aura": "r1_cn_thornbark"}], "aiHint": "Buff"},
        {"id": "r1_use_lanternlight_elixir", "name": "Lanternlight Elixir", "icon": "fire",
         "description": "Shadow resistance increased by 25 for 1 hour.", "school": "Physical", "time": "Gcd", "target": "Self",
         "hidden": True, "breaksStealth": True, "effects": [{"type": "ApplyAura", "aura": "r1_cn_lanternlight"}], "aiHint": "Buff"},
    ]


def item_auras():
    return [
        {"id": "r1_cn_thornbark", "name": "Thornbark Elixir", "icon": "vial", "kind": "Buff", "duration": 3600,
         "description": "Nature resistance increased by 25 and armor by 150.",
         "mods": [{"stat": "Resistance", "value": 25, "school": "Nature"}, {"stat": "Armor", "value": 150}]},
        {"id": "r1_cn_lanternlight", "name": "Lanternlight Elixir", "icon": "fire", "kind": "Buff", "duration": 3600,
         "description": "Shadow resistance increased by 25.", "mods": [{"stat": "Resistance", "value": 25, "school": "Shadow"}]},
    ]


EPIC_POOL = ["r1_ep_thornmaws_fang", "r1_ep_briarback_cleaver", "r1_ep_sporeweave_cloak", "r1_ep_wickglass_band",
             "r1_ep_sorrows_edge", "r1_ep_solaces_tear", "r1_ep_mossgrip_knuckles", "r1_ep_twinned_locket",
             "r1_ep_mirelight_crook", "r1_ep_bogiron_bludgeon", "r1_ep_witchlight_kris", "r1_ep_hollowstring_longbow"]


def loot_tables():
    return [
        {"id": "lt_r1_chest_offerings", "goldMin": 3000, "goldMax": 5000, "entries": [
            {"random": True, "chance": 100, "quality": "Rare", "itemLevelOffset": 3},
            {"item": "r1_heartwood_draught", "min": 2, "max": 3},
            {"item": "r1_lanternlight_elixir", "min": 1, "max": 2},
            {"pool": EPIC_POOL, "chance": 20, "partyUsable": True}]},
        {"id": "lt_r1_chest_larder", "goldMin": 2500, "goldMax": 4000, "entries": [
            {"random": True, "chance": 100, "quality": "Rare", "itemLevelOffset": 2},
            {"random": True, "chance": 60, "quality": "Uncommon", "itemLevelOffset": 2},
            {"item": "r1_glowcap_tonic", "min": 1, "max": 2},
            {"item": "r1_thornbark_elixir", "min": 1, "max": 2}]},
    ]


def bundle():
    return {"_note": "The Hollow Heart raid's people, talk, quests, items and chest loot (Docs/Expansion.md §8 row r1). "
                     "Generated by Tools/datagen/r1/gen_r1.py. Owner: r1.",
            "npcs": npcs(), "dialogues": dialogues(), "quests": quests(),
            "items": items(), "abilities": item_abilities(), "auras": item_auras(), "lootTables": loot_tables()}
