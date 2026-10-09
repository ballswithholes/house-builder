"""Shared helpers for the Mage/Warlock data generator (mage_warlock_gen).

Magnitudes are authored as WoW Classic rank-1 values; perLevel is computed so that the
value at the last rank level lands on the real max-rank value (avg of min/max for ranges).
"""
import json


def r3(x):
    x = round(float(x), 3)
    return int(x) if x == int(x) else x


def span(ranks):
    return (ranks[-1] - ranks[0]) if ranks and len(ranks) > 1 else 0


def per_level(v1, vN, ranks):
    s = span(ranks)
    return r3((vN - v1) / s) if s else 0


def train_cost(level):
    return max(10, int(round(level * level * 7.5 / 10.0)) * 10)


def mana(c1, cN=None, ranks=None):
    c = {"type": "Mana", "amount": c1}
    if cN is not None and ranks and span(ranks) and cN != c1:
        c["perLevel"] = per_level(c1, cN, ranks)
    return c


def dmg(lo, hi, loN=None, hiN=None, ranks=None, coef=0, **kw):
    e = {"type": "Damage", "min": lo}
    if hi is not None and hi != lo:
        e["max"] = hi
    if loN is not None and ranks and span(ranks):
        e["perLevel"] = per_level((lo + (hi if hi is not None else lo)) / 2.0,
                                  (loN + (hiN if hiN is not None else loN)) / 2.0, ranks)
    if coef:
        e["coef"] = coef
    e.update(kw)
    return e


def heal(lo, hi=None, loN=None, hiN=None, ranks=None, coef=0, **kw):
    e = dmg(lo, hi, loN, hiN, ranks, coef, **kw)
    e["type"] = "Heal"
    return e


def scaled(v1, vN, ranks):
    """min/perLevel pair for an effect whose magnitude scales by rank."""
    d = {"min": v1}
    if ranks and span(ranks) and vN != v1:
        d["perLevel"] = per_level(v1, vN, ranks)
    return d


def apply(aura, v1=None, vN=None, ranks=None, **kw):
    """ApplyAura. v1/vN/ranks are documentation only (the headline rank-1 -> max-rank value of the aura); the aura
    scales through its own mods' values/perLevel, tick/absorb perLevel. Tooltip {n} reads the aura directly."""
    e = {"type": "ApplyAura", "aura": aura}
    e.update(kw)
    return e


def mod(stat, value, pct=False, school=None, target=None, values=None, perLevel=None):
    """Aura/talent stat mod. For auras, `values` are per rank of the applying ability and `perLevel` is added per
    (effective level - learnLevel) of the applying ability (StatModDef.perLevel)."""
    m = {"stat": stat, "value": value}
    if values:
        assert values[0] == value, (stat, value, values)
        m["values"] = values
    if perLevel:
        m["perLevel"] = perLevel
    if pct:
        m["pct"] = True
    if school:
        m["school"] = school
    if target:
        m["target"] = target
    return m


def talent_level(tier):
    return 10 + 5 * (tier - 1)


ABILITY_ORDER = [
    "_note", "id", "name", "icon", "classId", "school", "description", "learnLevel", "rankLevels", "trainCost",
    "fromTalent", "passive", "passiveAura", "scaleWithLevel", "hidden", "time", "castTime", "channeled",
    "channelTicks", "gcdOverride", "cooldown", "cooldownGroup", "cost", "target", "range", "minRange", "melee",
    "area", "requires", "effects", "tags", "exclusiveGroup", "breaksStealth", "autoAttack", "usableWhileCasting",
    "special", "aiHint", "aiPriority",
]
AURA_ORDER = [
    "_note", "id", "name", "icon", "description", "kind", "school", "dispel", "duration", "maxStacks", "charges",
    "exclusiveGroup", "exclusivePerCaster", "tags", "hidden", "persistThroughDeath", "mods", "states",
    "forbidSchools", "absorb", "breakOnDamage", "breakDamageThreshold", "breakOnAction", "breakOnMove",
    "tickInterval", "tickEffects", "onApply", "onExpire", "onRemove", "procs", "radius", "radiusAura",
    "radiusAffects", "special",
]
EFFECT_ORDER = [
    "type", "target", "school", "min", "max", "perLevel", "perCombo", "coef", "apCoef", "pctOfMax", "pctOfDamage",
    "resource", "amount", "pctToCaster", "aura", "auraTag", "stacks", "duration", "chance", "radius", "dispelType",
    "dispelCount", "lockout", "distance", "summon", "lifetime", "count", "item", "ability", "abilities", "tags",
    "schools", "threat", "threatPct", "requireTargetAura", "consumeTargetAura", "cannotCrit", "cannotMiss", "special",
]
TALENT_ORDER = ["_note", "id", "name", "icon", "tier", "column", "maxRank", "requires", "description", "effects"]
ITEM_ORDER = [
    "_note", "id", "name", "icon", "description", "kind", "quality", "itemLevel", "requiredLevel", "equip",
    "armorType", "weaponType", "armor", "minDamage", "maxDamage", "speed", "damageSchool", "stats", "equipEffects",
    "use", "consumable", "stack", "price", "classes", "unique",
]
CREATURE_ORDER = [
    "_note", "id", "name", "description", "sprite", "portrait", "type", "family", "rank", "levelMin", "levelMax",
    "healthMult", "damageMult", "armorMult", "manaMult", "resource", "attackSpeed", "meleeSchool", "ranged",
    "rangedRange", "projectile", "moveSpeed", "size", "ai", "abilities", "passives", "immune", "stats", "xpMult",
]
EFFECT_TYPES = {"Damage", "WeaponDamage", "Heal", "ApplyAura", "RemoveAura", "Dispel", "Interrupt", "Taunt", "Threat",
                "GainResource", "DrainResource", "Teleport", "Charge", "Knockback", "Summon", "SummonTotem",
                "Resurrect", "CreateItem", "ResetCooldowns", "TriggerAbility", "Kill", "Special"}


def order(d, keys):
    if not isinstance(d, dict):
        return d
    out = {}
    for k in keys:
        if k in d:
            out[k] = d[k]
    for k in d:
        if k not in out:
            out[k] = d[k]
    return out


def deep_order_effects(v):
    if isinstance(v, list):
        return [deep_order_effects(x) for x in v]
    if isinstance(v, dict):
        if isinstance(v.get("type"), str) and v["type"] in EFFECT_TYPES:
            v = order(v, EFFECT_ORDER)
        return {k: deep_order_effects(x) for k, x in v.items()}
    return v


def clean(v):
    if isinstance(v, float):
        return r3(v)
    if isinstance(v, list):
        return [clean(x) for x in v]
    if isinstance(v, dict):
        return {k: clean(x) for k, x in v.items()}
    return v


def fmt(v, indent=0, width=118):
    s = json.dumps(v, ensure_ascii=False, separators=(", ", ": "))
    if not isinstance(v, (dict, list)) or len(s) + indent <= width or len(v) == 0:
        return s
    pad = " " * (indent + 2)
    if isinstance(v, dict):
        parts = []
        for k, x in v.items():
            key = json.dumps(k) + ": "
            parts.append(pad + key + fmt(x, indent + 2, width))
        return "{\n" + ",\n".join(parts) + "\n" + " " * indent + "}"
    if all(not isinstance(x, (dict, list)) for x in v):
        # pack scalars (talent ids, ability filters) several per line
        lines, cur = [], ""
        for x in v:
            item = json.dumps(x, ensure_ascii=False)
            if cur and len(pad) + len(cur) + len(item) + 2 > width:
                lines.append(pad + cur.rstrip())
                cur = ""
            cur += item + ", "
        lines.append(pad + cur.rstrip().rstrip(","))
        return "[\n" + "\n".join(lines) + "\n" + " " * indent + "]"
    parts = [pad + fmt(x, indent + 2, width) for x in v]
    return "[\n" + ",\n".join(parts) + "\n" + " " * indent + "]"


def write_bundle(path, bundle):
    out = {}
    for k, v in bundle.items():
        if k == "abilities":
            v = [order(a, ABILITY_ORDER) for a in v]
        elif k == "auras":
            v = [order(a, AURA_ORDER) for a in v]
        elif k == "items":
            v = [order(a, ITEM_ORDER) for a in v]
        elif k == "creatures":
            v = [order(a, CREATURE_ORDER) for a in v]
        elif k == "talentTrees":
            v = [dict(t, talents=[order(x, TALENT_ORDER) for x in t["talents"]]) for t in v]
        out[k] = deep_order_effects(clean(v))
    lines = ["{"]
    keys = list(out.keys())
    for i, k in enumerate(keys):
        v = out[k]
        comma = "," if i < len(keys) - 1 else ""
        if isinstance(v, list) and k != "_conventions":
            lines.append("  " + json.dumps(k) + ": [")
            for j, x in enumerate(v):
                c2 = "," if j < len(v) - 1 else ""
                lines.append("    " + fmt(x, 4) + c2)
            lines.append("  ]" + comma)
        else:
            lines.append("  " + json.dumps(k) + ": " + fmt(v, 2) + comma)
    lines.append("}")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
