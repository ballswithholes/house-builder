#!/usr/bin/env python3
"""The Hollow Heart raid (Docs/Expansion.md §8, builder r1): writes
   D/content/raid_hollow_heart.json            the map
   D/content/raid_hollow_heart_creatures.json  trash, adds, the four bosses, their abilities and auras
   D/content/raid_hollow_heart_people.json     npcs, dialogues, quests, items, chest loot tables
The JSON is the source of truth once committed; this script is how it was made (re-run it, then review the diff).
Run:  python3 Lanternvale/Tools/datagen/r1/gen_r1.py
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Assets", "Lanternvale", "Resources", "Data", "content"))
sys.path.insert(0, HERE)

import r1_map, r1_creatures, r1_people  # noqa: E402


def dump(v, ind=0, width=150):
    """JSON with short objects and lists on one line (one prop, enemy or choice per line)."""
    flat = json.dumps(v, ensure_ascii=False, separators=(", ", ": "))
    if len(flat) + ind <= width or not isinstance(v, (dict, list)) or not v:
        return flat
    pad, inner = " " * ind, " " * (ind + 2)
    if isinstance(v, dict):
        items = [f'{inner}{json.dumps(k)}: {dump(x, ind + 2, width)}' for k, x in v.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    items = [inner + dump(x, ind + 2, width) for x in v]
    return "[\n" + ",\n".join(items) + "\n" + pad + "]"


def write(name, bundle):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(dump(bundle))
        f.write("\n")
    print("wrote", os.path.relpath(path))


if __name__ == "__main__":
    write("raid_hollow_heart.json", r1_map.bundle())
    write("raid_hollow_heart_creatures.json", r1_creatures.bundle())
    write("raid_hollow_heart_people.json", r1_people.bundle())
