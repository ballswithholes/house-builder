"""Shared helpers for generating Lanternvale class DataBundles (Hunter / Shaman)."""
import json


def r(x, nd=3):
    """Round and print integral floats as ints."""
    v = round(float(x), nd)
    return int(v) if v == int(v) else v


def pl(v1, vmax, l1, lmax):
    """perLevel so that value(lmax) = vmax when value(l1) = v1."""
    return r((vmax - v1) / float(lmax - l1))


def train_cost(level):
    """WoW-Classic-like trainer cost in copper for a rank learned at `level`."""
    if level <= 1:
        return 10
    return int(round(level * level * 11.7 / 10.0)) * 10


def mana(amount, per_level=0):
    c = {"type": "Mana", "amount": r(amount)}
    if per_level:
        c["perLevel"] = r(per_level)
    return c


def focus(amount):
    return {"type": "Focus", "amount": r(amount)}


def clean(o):
    """Drop None values recursively (lets builders pass optional keys)."""
    if isinstance(o, dict):
        return {k: clean(v) for k, v in o.items() if v is not None}
    if isinstance(o, list):
        return [clean(x) for x in o]
    return o


def fmt(o, ind=0, width=124):
    one = json.dumps(o, ensure_ascii=False, separators=(", ", ": "))
    if not isinstance(o, (dict, list)) or len(one) + ind <= width:
        return one
    sp = " " * (ind + 2)
    if isinstance(o, list):
        if all(not isinstance(x, (dict, list)) for x in o):
            lines, cur = [], ""
            for x in o:
                t = json.dumps(x, ensure_ascii=False)
                if cur and len(cur) + len(t) + 2 + ind + 2 > width:
                    lines.append(cur.rstrip())
                    cur = ""
                cur += t + ", "
            if cur:
                lines.append(cur.rstrip().rstrip(","))
            lines = [ln if ln.endswith(",") or i == len(lines) - 1 else ln for i, ln in enumerate(lines)]
            return "[\n" + "\n".join(sp + ln for ln in lines) + "\n" + " " * ind + "]"
        return "[\n" + ",\n".join(sp + fmt(x, ind + 2, width) for x in o) + "\n" + " " * ind + "]"
    parts = []
    for k, v in o.items():
        kk = json.dumps(k) + ": "
        parts.append(sp + kk + fmt(v, ind + 2, width))
    return "{\n" + ",\n".join(parts) + "\n" + " " * ind + "}"


def write_bundle(path, bundle):
    bundle = clean(bundle)
    text = fmt(bundle) + "\n"
    json.loads(text)  # sanity
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    return bundle


class Builder:
    """Collects abilities/auras with the class id and default train costs filled in."""

    def __init__(self, class_id):
        self.cls = class_id
        self.abilities = []
        self.auras = []
        self.ids = set()
        self.aura_ids = set()

    def ability(self, id, name, icon, school, description, learn=1, ranks=None, **kw):
        assert id not in self.ids, id
        self.ids.add(id)
        d = {"id": id, "name": name, "icon": icon, "classId": self.cls, "school": school,
             "description": description, "learnLevel": learn}
        if ranks:
            assert ranks[0] == learn, (id, ranks, learn)
            assert all(b > a for a, b in zip(ranks, ranks[1:])), id
            d["rankLevels"] = ranks
        hidden = kw.get("hidden", False)
        talent = kw.get("fromTalent", False)
        if "trainCost" not in kw and not hidden and not talent:
            d["trainCost"] = train_cost(learn)
        order = ["trainCost", "fromTalent", "passive", "passiveAura", "scaleWithLevel", "hidden", "time", "castTime",
                 "channeled", "channelTicks", "gcdOverride", "cooldown", "cooldownGroup", "cost", "target", "range",
                 "minRange", "melee", "area", "requires", "effects", "tags", "exclusiveGroup", "breaksStealth",
                 "autoAttack", "nextSwing", "usableWhileCasting", "special", "aiHint", "aiPriority"]
        for k in kw:
            assert k in order, (id, k)
        for k in order:
            if k in kw and kw[k] is not None and kw[k] is not False:
                d[k] = kw[k]
        if not d.get("passive"):
            assert "aiHint" in d and "aiPriority" in d, id
        self.abilities.append(d)
        return d

    def aura(self, id, name, icon, description, kind="Buff", school="Physical", **kw):
        assert id not in self.aura_ids, id
        self.aura_ids.add(id)
        d = {"id": id, "name": name, "icon": icon, "description": description, "kind": kind, "school": school}
        order = ["dispel", "duration", "maxStacks", "charges", "exclusiveGroup", "exclusivePerCaster", "tags", "hidden",
                 "persistThroughDeath", "mods", "states", "forbidSchools", "absorb", "breakOnDamage",
                 "breakDamageThreshold", "breakOnAction", "breakOnMove", "tickInterval", "tickEffects", "onApply",
                 "onExpire", "onRemove", "procs", "radius", "radiusAura", "radiusAffects", "special"]
        for k in kw:
            assert k in order, (id, k)
        for k in order:
            if k in kw and kw[k] is not None and kw[k] is not False:
                d[k] = kw[k]
        self.auras.append(d)
        return d


def mod(stat, value=None, values=None, pct=None, school=None, target=None, perLevel=None):
    m = {"stat": stat}
    if values:
        m["value"] = values[0]
        m["values"] = values
    elif value is not None:
        m["value"] = r(value)
    if perLevel:
        m["perLevel"] = r(perLevel)
    if pct:
        m["pct"] = True
    if school:
        m["school"] = school
    if target:
        m["target"] = target
    return m


def eff(type, **kw):
    e = {"type": type}
    e.update({k: (r(v) if isinstance(v, float) else v) for k, v in kw.items() if v is not None})
    return e


def talent(id, name, icon, tier, column, max_rank, description, effects, requires=None):
    t = {"id": id, "name": name, "icon": icon, "tier": tier, "column": column, "maxRank": max_rank,
         "description": description}
    if requires:
        t["requires"] = requires
    t["effects"] = effects
    for e in effects:
        if "values" in e:
            assert len(e["values"]) == max_rank, (id, e)
    return t


def stat_p(stat, value=None, values=None, pct=False, school=None, target=None):
    p = {"type": "Stat", "stat": stat}
    if values:
        p["values"] = values
    else:
        p["value"] = r(value)
    if pct:
        p["pct"] = True
    if school:
        p["school"] = school
    if target:
        p["target"] = target
    return p


def amod(prop, value=None, values=None, abilities=None, tags=None, schools=None, target=None):
    p = {"type": "AbilityMod", "property": prop}
    if values:
        p["values"] = values
    else:
        p["value"] = r(value)
    if abilities:
        p["abilities"] = abilities
    if tags:
        p["tags"] = tags
    if schools:
        p["schools"] = schools
    if target:
        p["target"] = target
    return p


def proc_p(proc, values=None, target=None):
    p = {"type": "Proc"}
    if values:
        p["values"] = values
    if target:
        p["target"] = target
    p["proc"] = proc
    return p


def grant(ability):
    return {"type": "GrantAbility", "ability": ability}


def special_p(name, values=None):
    p = {"type": "Special", "special": name}
    if values:
        p["values"] = values
    return p


def check_build(trees, build):
    """Verify a default build respects tiers (5 per tier) and prerequisites, in order."""
    by_id = {}
    for tr in trees:
        for t in tr["talents"]:
            by_id[t["id"]] = (tr["id"], t)
    spent = {tr["id"]: 0 for tr in trees}
    ranks = {}
    for i, tid in enumerate(build):
        tree, t = by_id[tid]
        need = 5 * (t["tier"] - 1)
        assert spent[tree] >= need, f"point {i+1} {tid}: tier {t['tier']} needs {need} in {tree}, have {spent[tree]}"
        if t.get("requires"):
            req = by_id[t["requires"]][1]
            assert ranks.get(req["id"], 0) == req["maxRank"], f"{tid} requires {req['id']} maxed"
        ranks[tid] = ranks.get(tid, 0) + 1
        assert ranks[tid] <= t["maxRank"], tid
        spent[tree] += 1
    assert len(build) == 51, len(build)
    return spent
