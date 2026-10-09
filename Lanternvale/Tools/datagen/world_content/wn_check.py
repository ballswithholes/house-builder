"""Extra sanity checks for world content (maps walkability, flags, speakers, reachability)."""
import json, glob, os, math, re
from collections import deque

DATA = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data"
CONTENT = DATA + "/content"


def load(path):
    txt = open(path).read()
    return json.loads(txt)


bundles = {os.path.basename(p): load(p) for p in glob.glob(CONTENT + "/*.json")}
items = {i["id"]: i for b in bundles.values() for i in b.get("items", [])}
npcs = {n["id"]: n for b in bundles.values() for n in b.get("npcs", [])}
comps = {c["id"]: c for b in bundles.values() for c in b.get("companions", [])}
dlgs = {d["id"]: d for b in bundles.values() for d in b.get("dialogues", [])}
quests = {q["id"]: q for b in bundles.values() for q in b.get("quests", [])}
maps = {m["id"]: m for b in bundles.values() for m in b.get("maps", [])}
creatures = {c["id"]: c for b in bundles.values() for c in b.get("creatures", [])}
art = set(re.findall(r"`((?:bg|ground|decal|prop|fg|char|comp|npc|portrait|cr|pet|demon|totem|fx)_[a-z0-9_]+)`",
                     open("/home/user/house-builder/Lanternvale/Docs/ArtKeys.md").read()))
art |= {"portrait_" + k.split("_", 1)[1] for k in art if k.startswith(("char_", "comp_", "npc_"))}
problems = []
warn = []

# ---------------------------------------------------------------- id collisions with other folders
other = {}
for p in glob.glob(DATA + "/**/*.json", recursive=True):
    if p.startswith(CONTENT): continue
    try:
        b = json.loads(re.sub(r"//[^\n]*", "", open(p).read()))
    except Exception as e:
        warn.append(f"could not parse {p}: {e}")
        continue
    for k in ("items", "abilities", "auras", "creatures"):
        for x in b.get(k, []):
            other.setdefault(k, set()).add(x.get("id"))
mine = {k: {x["id"] for b in bundles.values() for x in b.get(k, [])} for k in ("items", "abilities", "auras", "creatures")}
for k in mine:
    for i in mine[k] & other.get(k, set()):
        problems.append(f"id collision with class data: {k} '{i}'")

# ---------------------------------------------------------------- art keys
def chk_art(key, where):
    if key and key not in art:
        problems.append(f"{where}: unknown art key '{key}'")

for n in npcs.values():
    chk_art(n.get("sprite"), f"npc {n['id']}"); chk_art(n.get("portrait"), f"npc {n['id']} portrait")
for c in comps.values():
    chk_art(c.get("sprite"), f"comp {c['id']}"); chk_art(c.get("portrait"), f"comp {c['id']} portrait")
for c in creatures.values():
    if c["id"].startswith("cr_"):
        chk_art(c.get("sprite"), f"creature {c['id']}")
        chk_art(c.get("projectile"), f"creature {c['id']} projectile")
for m in maps.values():
    for l in m["layers"]: chk_art(l["art"], f"map {m['id']} layer")
    chk_art(m["ground"], f"map {m['id']} ground")
    for p in m["props"] + m["foreground"]: chk_art(p["art"], f"map {m['id']} prop")
    for c in m.get("chests", []): chk_art(c.get("art", "prop_chest"), f"map {m['id']} chest")

# ---------------------------------------------------------------- maps
def in_ellipse(px, py, cx, cy, w, h, pad=0.0):
    a, b = w / 2 + pad, h / 2 + pad
    if a <= 0 or b <= 0: return False
    return ((px - cx) / a) ** 2 + ((py - cy) / b) ** 2 <= 1


for m in maps.values():
    W, Dp = m["width"], m["depth"]
    cols = [(p["pos"][0] + p.get("collider", {}).get("offset", [0, 0])[0], p["pos"][1], p["collider"]["w"],
             p["collider"]["h"], p["art"]) for p in m["props"] if p.get("collider", {}).get("w", 0) > 0]
    cols += [(c["pos"][0], c["pos"][1], 1.0, 0.6, "chest:" + c["id"]) for c in m.get("chests", [])]
    for p in m["props"]:
        x, y = p["pos"]
        if not (-0.5 <= x <= W + 0.5 and -1.0 <= y <= Dp + 0.5):
            problems.append(f"map {m['id']}: prop {p['art']} at {p['pos']} out of bounds")
    for p in m["foreground"]:
        if not (-1.5 <= p["pos"][1] <= 1.2):
            warn.append(f"map {m['id']}: fg {p['art']} at y {p['pos'][1]}")

    def blocked(x, y, pad=0.35):
        if not (0.2 <= x <= W - 0.2 and 0.2 <= y <= Dp - 0.2): return True
        return any(in_ellipse(x, y, cx, cy, w, h, pad) for cx, cy, w, h, _ in cols)

    # points of interest
    pois = []
    for s in m["spawns"]: pois.append((f"spawn {s['id']}", *s["pos"]))
    for n in m["npcs"]: pois.append((f"npc {n['npc']}", *n["pos"]))
    for c in m.get("chests", []): pois.append((f"chest {c['id']}", *c["pos"]))
    for t in m["transitions"]: pois.append((f"transition {t['id']}", *t["pos"]))
    for e in m["encounters"]: pois.append((f"encounter {e['id']}", *e["pos"]))
    for e in m["encounters"]:
        for en in e["enemies"]: pois.append((f"enemy {e['id']}/{en['creature']}", *en["pos"]))
    for name, x, y in pois:
        hit = [a for cx, cy, w, h, a in cols if in_ellipse(x, y, cx, cy, w, h) and a != "chest:" + name[6:]]
        if hit and not name.startswith("encounter"):
            problems.append(f"map {m['id']}: {name} at ({x},{y}) inside collider of {hit}")
    # grid BFS from default spawn
    step = 0.5
    nx, ny = int(W / step) + 1, int(Dp / step) + 1
    free = [[not blocked(i * step, j * step) for j in range(ny)] for i in range(nx)]
    sp = next(s for s in m["spawns"] if s["id"] == "default")["pos"]
    si, sj = round(sp[0] / step), round(sp[1] / step)
    seen = [[False] * ny for _ in range(nx)]
    if not free[si][sj]: problems.append(f"map {m['id']}: default spawn blocked")
    q = deque([(si, sj)]); seen[si][sj] = True
    while q:
        i, j = q.popleft()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            a, b = i + di, j + dj
            if 0 <= a < nx and 0 <= b < ny and free[a][b] and not seen[a][b]:
                seen[a][b] = True; q.append((a, b))

    def reachable(x, y, r=1.6):
        for i in range(max(0, int((x - r) / step)), min(nx, int((x + r) / step) + 2)):
            for j in range(max(0, int((y - r) / step)), min(ny, int((y + r) / step) + 2)):
                if seen[i][j] and math.hypot(i * step - x, j * step - y) <= r: return True
        return False

    for name, x, y in pois:
        if not reachable(x, y):
            problems.append(f"map {m['id']}: {name} at ({x},{y}) not reachable from default spawn")
    # encounter distances
    for e in m["encounters"]:
        ex, ey = e["pos"]; r = e["radius"]
        for s in m["spawns"]:
            d = math.dist((ex, ey), s["pos"])
            if d < r + 2.0: problems.append(f"map {m['id']}: encounter {e['id']} too close to spawn {s['id']} ({d:.1f})")
        for t in m["transitions"]:
            tx, ty = t["pos"]; tw, th = t["size"]
            cx = min(max(ex, tx - tw / 2), tx + tw / 2); cy = min(max(ey, ty - th / 2), ty + th / 2)
            if math.dist((ex, ey), (cx, cy)) < r + 0.5:
                problems.append(f"map {m['id']}: encounter {e['id']} overlaps transition {t['id']}")
        for n in m["npcs"]:
            d = math.dist((ex, ey), n["pos"])
            if d < r + 0.5 and not n.get("requireFlag"):
                warn.append(f"map {m['id']}: npc {n['npc']} inside encounter {e['id']} radius ({d:.1f})")
        for o in m["encounters"]:
            if o is e: continue
            d = math.dist((ex, ey), o["pos"])
            if d < r + o["radius"] and e["id"] < o["id"]:
                problems.append(f"map {m['id']}: encounters {e['id']} and {o['id']} overlap ({d:.1f})")
        for en in e["enemies"]:
            x, y = en["pos"]
            if not (0 <= x <= W and 0 <= y <= Dp): problems.append(f"map {m['id']}: enemy out of bounds {en}")
            if math.dist((x, y), (ex, ey)) > r + 2.5:
                warn.append(f"map {m['id']}: {e['id']} enemy {en['creature']} far from trigger")
    # chests away from encounters is fine (loot behind fights is intended)
    free_count = sum(sum(1 for v in col if v) for col in free)
    reach_count = sum(sum(1 for v in col if v) for col in seen)
    print(f"map {m['id']}: {len(m['props'])} props, {len(cols)} colliders, walkable cells reached "
          f"{reach_count}/{free_count}")

# ---------------------------------------------------------------- dialogues
flags_set, flags_used = set(), set()
for m in maps.values():
    for e in m["encounters"]:
        flags_set.add(e.get("doneFlag") or "enc_" + e["id"])
        if e.get("requireFlag"): flags_used.add(e["requireFlag"])
    for r in m["regions"]:
        if r.get("enterFlag"): flags_set.add(r["enterFlag"])
    for t in m["transitions"]:
        if t.get("requireFlag"): flags_used.add(t["requireFlag"])
    for n in m["npcs"]:
        for k in ("requireFlag", "hideFlag"):
            if n.get(k): flags_used.add(n[k])
    for c in m.get("chests", []):
        if c.get("requireFlag"): flags_used.add(c["requireFlag"])
for q in quests.values():
    for s in q["stages"]:
        for o in s["objectives"]:
            if o["type"] == "Flag": flags_used.add(o["target"])
            if o["type"] == "Reach" and not any(r["id"] == o["target"] for m in maps.values() for r in m["regions"]):
                problems.append(f"quest {q['id']}: Reach target {o['target']} is not a region")
        for oc in s.get("onComplete", []):
            if oc["type"] == "SetFlag": flags_set.add(oc["key"])

speakers_ok = set(npcs) | set(comps) | {"narrator", "player"}
for d in dlgs.values():
    ids = {n["id"] for n in d["nodes"]}
    edges = {n["id"]: set() for n in d["nodes"]}
    for n in d["nodes"]:
        if n["speaker"] not in speakers_ok: problems.append(f"dialogue {d['id']}.{n['id']}: unknown speaker {n['speaker']}")
        for c in n.get("conditions", []):
            if c["type"] in ("Flag", "NotFlag"): flags_used.add(c["key"])
            if c["type"] == "QuestState":
                if not any(s["id"] == c["value"] for s in quests[c["key"]]["stages"]):
                    problems.append(f"dialogue {d['id']}.{n['id']}: QuestState unknown stage {c['value']}")
        for o in n.get("outcomes", []):
            if o["type"] == "SetFlag": flags_set.add(o["key"])
            if o["type"] == "Recruit": flags_set.add("recruited_" + o["key"])
        if n.get("next"): edges[n["id"]].add(n["next"])
        if n.get("fallback"): edges[n["id"]].add(n["fallback"])
        for c in n.get("choices", []):
            for cc in c.get("conditions", []):
                if cc["type"] in ("Flag", "NotFlag"): flags_used.add(cc["key"])
                if cc["type"] == "QuestState" and not any(s["id"] == cc["value"] for s in quests[cc["key"]]["stages"]):
                    problems.append(f"dialogue {d['id']}.{n['id']}: QuestState unknown stage {cc['value']}")
            for o in c.get("outcomes", []):
                if o["type"] == "SetFlag": flags_set.add(o["key"])
                if o["type"] == "Recruit": flags_set.add("recruited_" + o["key"])
                if o["type"] == "StartCombat":
                    if not any(e["id"] == o["key"] for m in maps.values() for e in m["encounters"]):
                        problems.append(f"dialogue {d['id']}: StartCombat unknown encounter {o['key']}")
            if c.get("next"): edges[n["id"]].add(c["next"])
            if c.get("check"): edges[n["id"]] |= {c["check"]["success"], c["check"]["failure"]}
        for o in n.get("outcomes", []):
            if o["type"] == "StartCombat" and not any(e["id"] == o["key"] for m in maps.values() for e in m["encounters"]):
                problems.append(f"dialogue {d['id']}: StartCombat unknown encounter {o['key']}")
        # node with neither choices nor next ends the dialogue: fine. Text length check:
        sentences = len(re.findall(r"[.!?…](?:\s|$|\*)", n["text"]))
        if len(n["text"]) > 330: warn.append(f"dialogue {d['id']}.{n['id']}: long text ({len(n['text'])} chars)")
    seen = set(); q = deque([d["start"]])
    while q:
        x = q.popleft()
        if x in seen or x not in edges: continue
        seen.add(x); q.extend(edges[x])
    for u in ids - seen: warn.append(f"dialogue {d['id']}: node {u} unreachable")

for f in sorted(flags_used - flags_set):
    problems.append(f"flag '{f}' is checked but never set")
for f in sorted(flags_set - flags_used):
    warn.append(f"flag '{f}' set but never checked (fine if informational)")

# every npc with dialogue placed on a map (except speakers)
placed = {n["npc"] for m in maps.values() for n in m["npcs"]}
for n in npcs:
    if n not in placed: warn.append(f"npc {n} not placed on any map (speaker-only?)")
for c in comps:
    if c not in placed: problems.append(f"companion {c} not placed on any map")

node_count = sum(len(d["nodes"]) for d in dlgs.values())
print(f"dialogues {len(dlgs)}, nodes {node_count}, items {len(items)}, creatures {len(creatures)}, npcs {len(npcs)}")
print("\nWARNINGS:"); [print("  ", w) for w in warn]
print("\nPROBLEMS:"); [print("  ", p) for p in problems]
print("OK" if not problems else f"{len(problems)} problems")
