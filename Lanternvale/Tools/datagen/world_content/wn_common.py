"""Shared helpers for the Lanternvale WORLD CONTENT generator (scratch tooling, not shipped)."""
import json, os

OUT = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data/content"


def fmt(o, ind=0, width=150):
    one = json.dumps(o, ensure_ascii=False)
    if not isinstance(o, (dict, list)) or not o or len(one) + ind * 2 <= width:
        return one
    sp = "  " * (ind + 1)
    if isinstance(o, dict):
        parts = [f"{sp}{json.dumps(k, ensure_ascii=False)}: {fmt(v, ind + 1, width)}" for k, v in o.items()]
        return "{\n" + ",\n".join(parts) + "\n" + "  " * ind + "}"
    parts = [sp + fmt(v, ind + 1, width) for v in o]
    return "[\n" + ",\n".join(parts) + "\n" + "  " * ind + "]"


def write(name, bundle):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(fmt(bundle) + "\n")
    return path


# ------------------------------------------------------------------ stats

def st(*pairs, **kw):
    """st(Strength=3, Stamina=2) or st(("SpellDamage", 3, "Fire"))."""
    out = []
    for p in pairs:
        if len(p) == 3:
            out.append({"stat": p[0], "value": p[1], "school": p[2]})
        else:
            out.append({"stat": p[0], "value": p[1]})
    for k, v in kw.items():
        out.append({"stat": k, "value": v})
    return out


def pst(stat, value):
    return {"stat": stat, "value": value, "pct": True}


# ------------------------------------------------------------- dialogue

def cond(t, key="", value="", amount=None):
    d = {"type": t}
    if key: d["key"] = key
    if value: d["value"] = value
    if amount is not None: d["amount"] = amount
    return d


def out(t, key="", value="", amount=None):
    d = {"type": t}
    if key: d["key"] = key
    if value: d["value"] = value
    if amount is not None: d["amount"] = amount
    return d


# condition shorthands
def F(k): return cond("Flag", k)
def NF(k): return cond("NotFlag", k)
def QS(q, s): return cond("QuestState", q, s)
def QNS(q): return cond("QuestNotStarted", q)
def QA(q): return cond("QuestActive", q)
def QC(q): return cond("QuestComplete", q)
def HAS(i, n=1): return cond("HasItem", i, amount=n)
def NHAS(i): return cond("NotHasItem", i)
def CLS(c): return cond("Class", c)
def INP(c): return cond("InParty", c)
def NINP(c): return cond("NotInParty", c)
def GOLD(n): return cond("Gold", amount=n)
def LVL(n): return cond("Level", amount=n)

# outcome shorthands
def SET(k): return out("SetFlag", k)
def CLR(k): return out("ClearFlag", k)
def SQ(q): return out("StartQuest", q)
def STG(q, s): return out("SetQuestStage", q, s)
def CQ(q): return out("CompleteQuest", q)
def GIVE(i, n=1): return out("GiveItem", i, amount=n)
def TAKE(i, n=1): return out("TakeItem", i, amount=n)
def GG(n): return out("GiveGold", amount=n)
def TG(n): return out("TakeGold", amount=n)
def XP(n): return out("GiveXP", amount=n)
def RECRUIT(c): return out("Recruit", c)
def DISMISS(c): return out("Dismiss", c)
def FIGHT(enc): return out("StartCombat", enc)
def VENDOR(npc): return out("OpenVendor", npc)
def TRAIN(npc): return out("OpenTrainer", npc)
def RESPEC(npc=""): return out("OpenRespec", npc)
def REST(): return out("Rest")
def HEAL(): return out("HealParty")
def TP(m, s): return out("Teleport", m, s)
def APP(c, n): return out("Approval", c, amount=n)
def END(): return out("EndDialogue")
def SPECIAL(k, v=""): return out("Special", k, v)


def check(skill, dc, success, failure):
    return {"skill": skill, "dc": dc, "success": success, "failure": failure}


def C(text, next=None, cond=None, out=None, check_=None, once=False, tag=None):
    d = {"text": text}
    if next: d["next"] = next
    if cond: d["conditions"] = cond
    if out: d["outcomes"] = out
    if check_: d["check"] = check_
    if once: d["once"] = True
    if tag: d["tag"] = tag
    return d


def CHK(text, skill, dc, success, failure, cond=None, out=None, once=False, tag=None):
    """A skill-check choice; tag defaults to the upper-case skill name."""
    return C(text, cond=cond, out=out, check_=check(skill, dc, success, failure), once=once,
             tag=tag or skill.upper())


def N(id, speaker, text, choices=None, next=None, out=None, cond=None, fallback=None):
    d = {"id": id, "speaker": speaker, "text": text}
    if cond: d["conditions"] = cond
    if fallback: d["fallback"] = fallback
    if out: d["outcomes"] = out
    if choices: d["choices"] = choices
    if next: d["next"] = next
    return d


def D(id, start, nodes):
    return {"id": id, "start": start, "nodes": nodes}
