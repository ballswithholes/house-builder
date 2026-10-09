"""Totals the XP of the lv builder's content (Docs/Expansion.md §8: quest XP + normal fights; the dungeon adds ~1 level).
Kill XP = MobXp(player, mob) x rank mult x xpMult x rate(level); quest/dialogue XP = amount x rate(level)."""
import json, os, sys
D = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Assets", "Lanternvale", "Resources", "Data", "content"))
XP = [400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500, 7600, 8800, 10100, 11400, 12900, 14400, 16000]
RATE = [(1, 4), (12, 4), (18, 6), (24, 8), (30, 9), (60, 9)]
RANK = {"Elite": 2, "Rare": 2, "Boss": 3, "Minion": 0.25, "Normal": 1}


def rate(l):
    for (a, ra), (b, rb) in zip(RATE, RATE[1:]):
        if a <= l <= b:
            return ra + (rb - ra) * (l - a) / (b - a)
    return 9


def mobxp(p, m):
    base = p * 5 + 45
    if m >= p:
        return base * (1 + 0.05 * min(4, m - p))
    grey = p - 5 - p // 10
    if m <= grey:
        return 0
    zd = 7 if p < 12 else 8
    return max(0, base * (1 - (p - m) / zd))


def load(name):
    return json.load(open(os.path.join(D, name)))


lv2, dg1 = load("lv2_content.json"), load("dgn_root_hollows_content.json")
cre = {c["id"]: c for b in (lv2, dg1) for c in b["creatures"]}
cre["cr_boar"] = {"rank": "Normal", "levelOffset": 0}
maps = {m["id"]: m for m in load("map_lanternvale.json")["maps"] + load("dgn_root_hollows.json")["maps"]}


def kill_xp(enc, p):
    tot = 0
    for e in enc["enemies"]:
        c = cre[e["creature"]]
        lvl = p + c.get("levelOffset", 0)
        lvl = max(c.get("levelFloor", 1), min(c.get("levelCap", 63) or 63, lvl))
        tot += mobxp(p, lvl) * RANK[c["rank"]] * c.get("xpMult", 1) * rate(p)
    return tot


def dialogue_xp(bundle):
    return sum(o["amount"] for d in bundle["dialogues"] for n in d["nodes"] for o in n.get("outcomes", []) if o["type"] == "GiveXP")


for title, mapid, prefix, bundle, p in [("Lanternvale north band", "lanternvale", "enc_lv2", lv2, 11), ("The Root Hollows", "dgn_root_hollows", "enc_dg1", dg1, 12)]:
    kills = {e["id"]: kill_xp(e, p) for e in maps[mapid]["encounters"] if e["id"].startswith(prefix)}
    quests = {q["id"]: q["rewards"]["xp"] * rate(p) for q in bundle["quests"]}
    dlg = dialogue_xp(bundle) * rate(p) * 0.5   # the checks are once-only and branch: about half is earned
    total = sum(kills.values()) + sum(quests.values()) + dlg
    print(f"== {title} (party level {p}; {XP[p - 1]} XP to level {p + 1})")
    for k, v in kills.items():
        print(f"   kill  {k:32s} {v:7.0f}")
    for k, v in quests.items():
        print(f"   quest {k:32s} {v:7.0f}")
    print(f"   dialogue checks (about half)          {dlg:7.0f}")
    print(f"   TOTAL {total:7.0f} = {total / XP[p - 1]:.2f} levels at {p}")
# the village quests added to dialogues_village.json are counted with their quests above (lv2_singing_roots,
# lv2_lantern_oil): Tobben's +100 for sharing the oil is a branch bonus.
