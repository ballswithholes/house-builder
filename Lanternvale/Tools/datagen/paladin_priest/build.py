# Assemble paladin.json / priest.json DataBundles.
import importlib
import sys
from common import dumps

OUT = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data/classes/"


def check_unique(items, kind):
    seen = set()
    for x in items:
        if x["id"] in seen:
            raise SystemExit(f"duplicate {kind} {x['id']}")
        seen.add(x["id"])


def build(cls):
    mods = [importlib.import_module(m) for m in (f"{cls}_a", f"{cls}_b", f"{cls}_c")]
    tal = importlib.import_module(f"{cls}_talents")
    cdef = importlib.import_module(f"{cls}_class")
    abilities = [a for m in mods for a in m.ABILITIES]
    auras = [a for m in mods for a in m.AURAS] + tal.AURAS
    check_unique(abilities, "ability")
    check_unique(auras, "aura")
    bundle = {
        "_note": f"{cls.capitalize()} - WoW Classic 1.12 kit translated to Lanternvale. Generated; see specials for "
                 "custom handlers. Aura StatMod.values[i] = value when applied by rank i+1 of the source "
                 "ability/talent; ApplyAura duration + perLevel = seconds of duration per level above learnLevel.",
        "classes": [cdef.CLASS],
        "abilities": abilities,
        "auras": auras,
        "talentTrees": tal.TREES,
        "items": cdef.ITEMS,
    }
    if hasattr(cdef, "CREATURES"):
        bundle["creatures"] = cdef.CREATURES
    bundle["specials"] = cdef.SPECIALS
    text = dumps(bundle) + "\n"
    with open(OUT + f"{cls}.json", "w") as f:
        f.write(text)
    ntal = {t["name"]: len(t["talents"]) for t in tal.TREES}
    print(f"{cls}: {len(abilities)} abilities ({sum(1 for a in abilities if a.get('fromTalent'))} from talents, "
          f"{sum(1 for a in abilities if a.get('hidden'))} hidden), {len(auras)} auras, talents {ntal}, "
          f"{len(cdef.ITEMS)} items, {len(cdef.SPECIALS)} specials")


if __name__ == "__main__":
    for c in sys.argv[1:]:
        build(c)
