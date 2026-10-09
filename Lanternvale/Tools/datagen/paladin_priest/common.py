# Helpers for authoring Lanternvale class data (Paladin / Priest).
import json

ABILITY_ORDER = ["id", "name", "icon", "classId", "school", "description", "learnLevel", "rankLevels", "trainCost",
                 "fromTalent", "passive", "passiveAura", "scaleWithLevel", "hidden", "time", "castTime", "channeled",
                 "channelTicks", "gcdOverride", "cooldown", "cooldownGroup", "cost", "target", "range", "minRange",
                 "melee", "area", "requires", "effects", "tags", "exclusiveGroup", "breaksStealth", "autoAttack",
                 "nextSwing", "generatesComboPoint", "usableWhileCasting", "special", "aiHint", "aiPriority"]
AURA_ORDER = ["id", "name", "icon", "description", "kind", "school", "dispel", "duration", "maxStacks", "charges",
              "exclusiveGroup", "exclusivePerCaster", "tags", "hidden", "persistThroughDeath", "mods", "states",
              "forbidSchools", "absorb", "breakOnDamage", "breakDamageThreshold", "breakOnAction", "breakOnMove",
              "tickInterval", "tickEffects", "onApply", "onExpire", "onRemove", "procs", "radius", "radiusAura",
              "radiusAffects", "special"]
EFFECT_ORDER = ["type", "target", "school", "min", "max", "perLevel", "perCombo", "coef", "apCoef", "weaponPct",
                "offHand", "ranged", "pctOfMax", "pctOfDamage", "pctToCaster", "threat", "threatPct", "resource",
                "amount", "aura", "auraTag", "stacks", "duration", "durationPerCombo", "chance", "radius",
                "dispelType", "dispelCount", "lockout", "distance", "summon", "totemElement", "lifetime", "item",
                "count", "ability", "abilities", "tags", "schools", "chainTargets", "chainFalloffPct", "chainRange",
                "requireTargetAura", "consumeTargetAura", "cannotCrit", "cannotMiss", "special"]


def ordered(d, order):
    out = {}
    for k in order:
        if k in d:
            out[k] = d[k]
    for k in d:
        if k not in out:
            raise KeyError(f"unexpected key {k!r} in {d.get('id', d.get('type', '?'))}")
    return out


def r(x, nd=3):
    """Round floats, drop .0"""
    if isinstance(x, float):
        x = round(x, nd)
        if x == int(x):
            return int(x)
    return x


def pl(v1, vN, l1, lN):
    """perLevel so that value(lN) = vN given value(l1) = v1."""
    if lN == l1:
        return 0
    return r((vN - v1) / (lN - l1))


def interp(v1, vN, levels):
    """Linear per-rank integer values from rank-1 value to max-rank value across rank levels."""
    l1, lN = levels[0], levels[-1]
    return [int(round(v1 + (vN - v1) * (L - l1) / (lN - l1))) for L in levels]


def train_cost(level):
    """Approximate WoW Classic trainer cost (copper) for a spell rank learned at `level`."""
    if level <= 1:
        return 10
    if level < 10:
        return level * 25
    return int(round(level ** 3 * 0.2 / 10.0) * 10)


# ------------------------------------------------------------------ effects

def E(type_, **kw):
    d = {"type": type_}
    d.update(kw)
    return ordered({k: r(v) for k, v in d.items()}, EFFECT_ORDER)


def ranged_mag(type_, lo, hi, maxlo, maxhi, l1, lN, **kw):
    per = pl((lo + hi) / 2.0, (maxlo + maxhi) / 2.0, l1, lN)
    d = dict(min=lo, max=hi)
    if per:
        d["perLevel"] = per
    d.update(kw)
    return E(type_, **d)


def dmg(lo, hi, maxlo, maxhi, levels, **kw):
    return ranged_mag("Damage", lo, hi, maxlo, maxhi, levels[0], levels[-1], **kw)


def heal(lo, hi, maxlo, maxhi, levels, **kw):
    return ranged_mag("Heal", lo, hi, maxlo, maxhi, levels[0], levels[-1], **kw)


def apply(aura, **kw):
    return E("ApplyAura", aura=aura, **kw)


def cost(amount, maxAmount=None, levels=None, **kw):
    d = {"type": "Mana", "amount": amount}
    if maxAmount is not None and levels and len(levels) > 1:
        d["perLevel"] = pl(amount, maxAmount, levels[0], levels[-1])
    d.update(kw)
    return {k: r(v) for k, v in d.items()}


def M(stat, value=None, values=None, pct=False, school=None):
    d = {"stat": stat}
    if values:
        d["value"] = values[0]
        d["values"] = values
    else:
        d["value"] = value
    if pct:
        d["pct"] = True
    if school:
        d["school"] = school
    return d


def proc(trigger, effects, **kw):
    d = {"trigger": trigger}
    d.update(kw)
    d["effects"] = effects
    return d


# ---------------------------------------------------------- abilities/auras

def make_ability(class_id, id_, name, icon, school, desc, learn, ranks=None, **kw):
    d = dict(id=id_, name=name, icon=icon, classId=class_id, school=school, description=desc, learnLevel=learn)
    if ranks:
        assert ranks[0] == learn, id_
        d["rankLevels"] = ranks
    # talent abilities: rank 1 comes from the talent; trainCost still prices the later ranks
    if "trainCost" not in kw and not kw.get("hidden") and not (kw.get("fromTalent") and not ranks):
        d["trainCost"] = train_cost(learn)
    d.update(kw)
    return ordered(d, ABILITY_ORDER)


def make_aura(id_, name, icon, kind, school, desc, duration=0, **kw):
    d = dict(id=id_, name=name, icon=icon, description=desc, kind=kind, school=school, duration=duration)
    d.update(kw)
    return ordered(d, AURA_ORDER)


def talent(id_, name, icon, tier, column, maxRank, desc, effects, requires=None):
    d = dict(id=id_, name=name, icon=icon, tier=tier, column=column, maxRank=maxRank, description=desc)
    if requires:
        d["requires"] = requires
    d["effects"] = effects
    return d


def P_stat(stat, value=None, values=None, pct=False, school=None, target=None):
    d = {"type": "Stat", "stat": stat}
    if values:
        d["values"] = values
    else:
        d["value"] = value
    if pct:
        d["pct"] = True
    if school:
        d["school"] = school
    if target:
        d["target"] = target
    return d


def P_mod(prop, value=None, values=None, abilities=None, tags=None, schools=None):
    d = {"type": "AbilityMod", "property": prop}
    if values:
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


def P_proc(proc_def, values=None):
    d = {"type": "Proc", "proc": proc_def}
    if values:
        d["values"] = values
    return d


def P_grant(ability):
    return {"type": "GrantAbility", "ability": ability}


def P_special(special, values=None):
    d = {"type": "Special", "special": special}
    if values:
        d["values"] = values
    return d


# ------------------------------------------------------------------- output

def dumps(o, indent=0, width=118):
    s = json.dumps(o, ensure_ascii=False)
    if not isinstance(o, (dict, list)) or len(s) + indent <= width:
        return s
    pad = " " * (indent + 2)
    end = " " * indent
    if isinstance(o, dict):
        parts = []
        for k, v in o.items():
            parts.append(pad + json.dumps(k) + ": " + dumps(v, indent + 2, width))
        return "{\n" + ",\n".join(parts) + "\n" + end + "}"
    if all(not isinstance(x, (dict, list)) for x in o):
        toks = [json.dumps(x, ensure_ascii=False) for x in o]
        lines, cur = [], []
        for t in toks:
            if cur and len(pad) + len(", ".join(cur + [t])) + 1 > width:
                lines.append(", ".join(cur) + ",")
                cur = []
            cur.append(t)
        lines.append(", ".join(cur))
        return "[\n" + "\n".join(pad + ln for ln in lines) + "\n" + end + "]"
    return "[\n" + ",\n".join(pad + dumps(x, indent + 2, width) for x in o) + "\n" + end + "]"
