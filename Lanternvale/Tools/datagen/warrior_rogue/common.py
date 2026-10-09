"""Helpers for authoring Lanternvale class data as Python, emitted as JSON DataBundles."""
import json

def tc(level):
    """Approximate WoW Classic trainer cost (copper) for an ability first trainable at `level`."""
    if level <= 1:
        return 10
    if level < 4:
        return 100
    return max(100, int(round(level * level * 5.5 / 100.0)) * 100)

def E(type_, **kw):
    d = {"type": type_}
    d.update(kw)
    return d

def area(r, caster=True, max_t=0, affects=None, shape="Circle", angle=None):
    a = {"shape": shape, "radius": r}
    if caster:
        a["centeredOnCaster"] = True
    if max_t:
        a["maxTargets"] = max_t
    if affects:
        a["affects"] = affects
    if angle:
        a["angle"] = angle
    return a

def ab(id_, name, icon, desc, ranks=None, **kw):
    d = {"id": id_, "name": name, "icon": icon, "description": desc}
    if ranks:
        d["learnLevel"] = ranks[0]
        if len(ranks) > 1:
            d["rankLevels"] = list(ranks)
        if "trainCost" not in kw and not kw.get("fromTalent") and not kw.get("hidden"):
            d["trainCost"] = tc(ranks[0])
    d.update(kw)
    return d

def aura(id_, name, icon, desc, **kw):
    d = {"id": id_, "name": name, "icon": icon, "description": desc}
    d.update(kw)
    return d

def mod(stat, value, pct=False, school=None):
    m = {"stat": stat, "value": value}
    if pct:
        m["pct"] = True
    if school:
        m["school"] = school
    return m

def proc(trigger, effects, **kw):
    p = {"trigger": trigger}
    p.update(kw)
    p["effects"] = effects
    return p

# ---- talent passives
def P_stat(stat, value=None, values=None, pct=False, school=None):
    d = {"type": "Stat", "stat": stat}
    if values is not None:
        d["values"] = values
    else:
        d["value"] = value
    if pct:
        d["pct"] = True
    if school:
        d["school"] = school
    return d

def P_mod(prop, value=None, values=None, abilities=None, tags=None, schools=None):
    d = {"type": "AbilityMod", "property": prop}
    if values is not None:
        d["values"] = values
    else:
        d["value"] = value
    if abilities:
        d["abilities"] = abilities
    if tags:
        d["tags"] = tags
    if schools:
        d["schools"] = schools
    return d

def P_proc(p, values=None):
    d = {"type": "Proc", "proc": p}
    if values is not None:
        d["values"] = values
    return d

def P_grant(ability):
    return {"type": "GrantAbility", "ability": ability}

def P_special(special, **kw):
    d = {"type": "Special", "special": special}
    d.update(kw)
    return d

def T(id_, name, icon, tier, col, max_rank, desc, effects, requires=None):
    d = {"id": id_, "name": name, "icon": icon, "tier": tier, "column": col, "maxRank": max_rank,
         "description": desc}
    if requires:
        d["requires"] = requires
    d["effects"] = effects
    return d

# ---- compact pretty JSON writer
def dumps(o, indent=0, width=118):
    flat = json.dumps(o, ensure_ascii=False)
    if len(flat) + indent <= width or not isinstance(o, (dict, list)) or len(o) == 0:
        return flat
    pad = " " * (indent + 2)
    if isinstance(o, dict):
        items = [pad + json.dumps(k) + ": " + dumps(v, indent + 2, width).lstrip() for k, v in o.items()]
        return "{\n" + ",\n".join(items) + "\n" + " " * indent + "}"
    items = [pad + dumps(v, indent + 2, width).lstrip() for v in o]
    return "[\n" + ",\n".join(items) + "\n" + " " * indent + "]"

def finish_abilities(abilities, class_id):
    for a in abilities:
        a.setdefault("classId", class_id)
        # stable key order: id/name/icon/classId first
    out = []
    for a in abilities:
        head = {k: a[k] for k in ("id", "name", "icon", "classId") if k in a}
        rest = {k: v for k, v in a.items() if k not in head}
        head.update(rest)
        out.append(head)
    return out

def check_unique(items, kind):
    seen = set()
    for i in items:
        if i["id"] in seen:
            raise SystemExit(f"duplicate {kind} id {i['id']}")
        seen.add(i["id"])

def check_build(trees, build, start_level=10):
    """Verify a defaultBuild respects tier gating (5 x (tier-1) points in tree), prerequisites and maxRank."""
    tal = {}
    for t in trees:
        for x in t["talents"]:
            tal[x["id"]] = (t["id"], x)
    spent_tree = {}
    ranks = {}
    for i, tid in enumerate(build):
        tree, x = tal[tid]
        need = 5 * (x["tier"] - 1)
        have = spent_tree.get(tree, 0)
        if have < need:
            raise SystemExit(f"build point {i+1} ({tid}) needs {need} points in {tree}, has {have}")
        req = x.get("requires")
        if req and ranks.get(req, 0) < tal[req][1]["maxRank"]:
            raise SystemExit(f"build point {i+1} ({tid}) requires maxed {req}")
        ranks[tid] = ranks.get(tid, 0) + 1
        if ranks[tid] > x["maxRank"]:
            raise SystemExit(f"too many points in {tid}")
        spent_tree[tree] = have + 1
    if len(build) != 51:
        raise SystemExit(f"build has {len(build)} points")
    return spent_tree

def collect_specials(obj, out):
    if isinstance(obj, dict):
        s = obj.get("special")
        if s:
            out.add(s)
        for v in obj.values():
            collect_specials(v, out)
    elif isinstance(obj, list):
        for v in obj:
            collect_specials(v, out)
    return out

def collect_refs(obj, key, out):
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == key and isinstance(v, str) and v:
                out.add(v)
            collect_refs(v, key, out)
    elif isinstance(obj, list):
        for v in obj:
            collect_refs(v, key, out)
    return out

def write_bundle(path, note, classes, abilities, auras, trees, items, specials, extra_specials=()):
    check_unique(abilities, "ability"); check_unique(auras, "aura"); check_unique(items, "item")
    aura_ids = {a["id"] for a in auras}
    for ref in collect_refs([abilities, auras, trees, items], "aura", set()):
        if ref not in aura_ids:
            raise SystemExit(f"unknown aura ref {ref}")
    used = collect_specials([classes, abilities, auras, trees, items], set())
    documented = {s["id"] for s in specials} | set(extra_specials)
    missing = used - documented
    if missing:
        raise SystemExit(f"undocumented specials {missing}")
    unused = {s["id"] for s in specials} - used - set(extra_specials)
    bundle = {"_note": note, "classes": classes, "abilities": abilities, "auras": auras, "talentTrees": trees,
              "items": items, "specials": specials}
    with open(path, "w") as f:
        f.write(dumps(bundle) + "\n")
    return used, unused

def vmod(stat, values, pct=False, school=None):
    """Aura stat mod with per-rank values (rank of the applying ability, or talent rank for talent procs)."""
    m = {"stat": stat, "value": values[0], "values": list(values)}
    if pct:
        m["pct"] = True
    if school:
        m["school"] = school
    return m
