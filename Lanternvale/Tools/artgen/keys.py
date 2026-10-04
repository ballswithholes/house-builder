"""Key catalog: parses Docs/ArtKeys.md (+ the glyph list in Docs/DataSchema.md) so the generator can
verify that every documented key is produced, and defines manifest conventions.
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))          # .../Lanternvale
DOCS = os.path.join(ROOT, "Docs")
ART_DIR = os.path.join(ROOT, "Assets", "Lanternvale", "Resources", "Art")
PREVIEW_DIR = os.path.join(DOCS, "art_previews")

# Folder per manifest category (paths in the manifest are Resources-relative: "Art/<Folder>/<key>").
CATEGORY_DIR = {
    "Background": "Backgrounds",
    "Ground": "Ground",
    "Prop": "Props",
    "Foreground": "Foreground",
    "Character": "Characters",
    "Portrait": "Portraits",
    "Creature": "Creatures",
    "Effect": "Effects",
    "Icon": "Icons",
    "UI": "UI",
}

PREFIXES = ("bg", "ground", "decal", "prop", "fg", "char", "comp", "npc", "portrait", "cr", "pet", "demon",
            "totem", "fx", "ui", "logo", "glyph", "crest")
KEY_RE = re.compile(r"`((?:%s)_[a-z0-9_]+)`" % "|".join(PREFIXES))


def glyph_names(schema_path=None):
    """Glyph names from the "Icon glyphs" section of DataSchema.md."""
    schema_path = schema_path or os.path.join(DOCS, "DataSchema.md")
    with open(schema_path, encoding="utf-8") as f:
        text = f.read()
    sec = text.split("## Icon glyphs", 1)[1]
    block = sec.split("```", 2)[1]
    return [g for g in block.split() if re.fullmatch(r"[a-z0-9_]+", g)]


def required_keys(artkeys_path=None, schema_path=None):
    """Ordered list of every key the docs require."""
    artkeys_path = artkeys_path or os.path.join(DOCS, "ArtKeys.md")
    with open(artkeys_path, encoding="utf-8") as f:
        text = f.read()
    ordered = []
    seen = set()

    def add(k):
        if k not in seen:
            seen.add(k)
            ordered.append(k)

    for k in KEY_RE.findall(text):
        add(k)
    # portraits for every char_/comp_/npc_ key
    for k in list(ordered):
        if k.startswith(("char_", "comp_", "npc_")):
            add("portrait_" + k.split("_", 1)[1])
    # class crests (one per char_<class>)
    for k in list(ordered):
        if k.startswith("char_"):
            add("crest_" + k.split("_", 1)[1])
    for g in glyph_names(schema_path):
        add("glyph_" + g)
    return ordered


if __name__ == "__main__":
    ks = required_keys()
    print(len(ks), "keys")
    from collections import Counter
    print(Counter(k.split("_")[0] for k in ks))
