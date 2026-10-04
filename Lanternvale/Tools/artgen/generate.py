#!/usr/bin/env python3
"""Lanternvale placeholder art generator.

Paints every art key listed in Docs/ArtKeys.md (plus portraits, class crests and one glyph per
icon name in Docs/DataSchema.md) into Assets/Lanternvale/Resources/Art/<Folder>/<key>.png and
writes Resources/Art/art_manifest.json.

    python3 Lanternvale/Tools/artgen/generate.py            # everything (parallel)
    python3 Lanternvale/Tools/artgen/generate.py --only prop_ char_warrior   # subset (prefix match)
    python3 Lanternvale/Tools/artgen/generate.py --manifest-only            # re-write manifest + checks
    python3 Lanternvale/Tools/artgen/generate.py --list                     # print the key list

Deterministic: every painter seeds its random generators from the key name, so re-running
produces byte-identical PNGs. Requires Python 3.8+, numpy and Pillow.

Manifest conventions
  path    Resources-relative path without extension, e.g. "Art/Props/prop_cottage_a".
  height  world metres of the FULL image height (the engine sets pixels-per-unit = texture height / height).
          For props/creatures/foreground it is measured from the painted content so the object itself
          has the catalogue height; characters share one scale (canvas = 2.2 m, a human figure = 1.8 m).
  pivot   normalised; feet/base line of standing sprites.
"""
import argparse
import importlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import numpy as np  # noqa: E402
from PIL import Image  # noqa: E402

import keys as K  # noqa: E402

MODULES = ["environment", "props", "characters", "creatures", "fx", "icons", "ui"]


class Spec:
    __slots__ = ("key", "category", "size", "height", "measure", "pivot", "loop", "shadow", "tile_y")

    def __init__(self, key, category, size, height=None, measure=None, pivot=(0.5, 0.05), loop=False,
                 shadow=False, tile_y=False):
        self.key = key
        self.category = category
        self.size = tuple(size)
        self.height = height
        self.measure = measure
        self.pivot = tuple(float(v) for v in pivot)
        self.loop = loop
        self.shadow = shadow
        self.tile_y = tile_y

    @property
    def rel_path(self):
        return "%s/%s" % (K.CATEGORY_DIR[self.category], self.key)

    @property
    def file(self):
        return os.path.join(K.ART_DIR, K.CATEGORY_DIR[self.category], self.key + ".png")


class Registry:
    def __init__(self):
        self.specs = {}
        self.jobs = []

    def spec(self, key, category, **kw):
        if key in self.specs:
            raise ValueError("duplicate spec " + key)
        self.specs[key] = Spec(key, category, **kw)

    def job(self, keys, fn, *args):
        self.jobs.append((tuple(keys), fn, args))


_REG = None


def registry():
    global _REG
    if _REG is None:
        reg = Registry()
        for name in MODULES:
            importlib.import_module(name).register(reg)
        _REG = reg
    return _REG


def is_pow2(n):
    return n > 0 and (n & (n - 1)) == 0


def _run_job(idx):
    reg = registry()
    keys, fn, args = reg.jobs[idx]
    t0 = time.time()
    imgs = fn(*args)
    out = []
    for k, im in imgs.items():
        sp = reg.specs[k]
        if im.size != sp.size:
            raise RuntimeError("%s: painted size %s != spec %s" % (k, im.size, sp.size))
        os.makedirs(os.path.dirname(sp.file), exist_ok=True)
        im.save(sp.file, optimize=True)
        out.append((k, os.path.getsize(sp.file)))
    return keys, out, time.time() - t0


def measured_height(sp, im):
    """World height of the full image such that the painted object (pivot line -> top of content)
    measures sp.measure metres."""
    a = np.asarray(im.getchannel("A"), dtype=np.float32)
    H = a.shape[0]
    rows = np.where(a.max(axis=1) > 24)[0]
    if not len(rows):
        return float(sp.measure)
    top = rows[0]
    pivot_row = H * (1.0 - sp.pivot[1])
    content = max(1.0, pivot_row - top)
    return round(float(sp.measure) * H / content, 3)


def tile_error(im, axis):
    """Seam error in 0..255 units: |first - last| column (or row) vs. typical neighbour difference."""
    a = np.asarray(im.convert("RGBA"), dtype=np.float32)
    pm = a[..., :3] * (a[..., 3:4] / 255.0)
    pm = np.concatenate([pm, a[..., 3:4]], axis=2)
    if axis == 1:
        seam = np.abs(pm[:, 0] - pm[:, -1]).mean()
        typ = np.abs(np.diff(pm, axis=1)).mean()
    else:
        seam = np.abs(pm[0] - pm[-1]).mean()
        typ = np.abs(np.diff(pm, axis=0)).mean()
    return float(seam), float(typ)


def write_manifest(reg, order):
    entries = []
    problems = []
    total = 0
    for key in order:
        sp = reg.specs[key]
        if not os.path.exists(sp.file):
            problems.append("missing file for %s (%s)" % (key, sp.file))
            continue
        total += os.path.getsize(sp.file)
        im = Image.open(sp.file)
        im.load()
        w, h = im.size
        if (w, h) != sp.size:
            problems.append("%s: file size %s != spec %s" % (key, im.size, sp.size))
        if not (is_pow2(w) and is_pow2(h)):
            problems.append("%s: not power-of-two (%dx%d)" % (key, w, h))
        if sp.loop:
            seam, typ = tile_error(im, 1)
            if seam > 3.0 * typ + 2.0:
                problems.append("%s: horizontal seam %.2f (typical %.2f)" % (key, seam, typ))
        if sp.tile_y:
            seam, typ = tile_error(im, 0)
            if seam > 3.0 * typ + 2.0:
                problems.append("%s: vertical seam %.2f (typical %.2f)" % (key, seam, typ))
        height = sp.height if sp.measure is None else measured_height(sp, im)
        entries.append({
            "key": key,
            "path": "Art/" + sp.rel_path,
            "height": round(float(height), 3),
            "pivot": [round(sp.pivot[0], 3), round(sp.pivot[1], 3)],
            "category": sp.category,
            "loop": bool(sp.loop),
            "shadow": bool(sp.shadow),
        })
    path = os.path.join(K.ART_DIR, "art_manifest.json")
    os.makedirs(K.ART_DIR, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write('{\n  "entries": [\n')
        f.write(",\n".join("    " + json.dumps(e, separators=(", ", ": ")) for e in entries))
        f.write("\n  ]\n}\n")
    return entries, problems, total


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", nargs="*", help="render only keys starting with these prefixes")
    ap.add_argument("--jobs", type=int, default=os.cpu_count() or 2)
    ap.add_argument("--manifest-only", action="store_true")
    ap.add_argument("--list", action="store_true")
    args = ap.parse_args(argv)

    reg = registry()
    required = K.required_keys()
    missing = [k for k in required if k not in reg.specs]
    extra = [k for k in reg.specs if k not in set(required)]
    if args.list:
        for k in required:
            print(k)
        return 0
    if missing:
        print("ERROR: %d keys from ArtKeys.md/DataSchema.md have no painter:" % len(missing))
        for k in missing:
            print("   ", k)
        return 1
    if extra:
        print("note: %d extra keys not in the docs: %s" % (len(extra), ", ".join(extra)))
    unjobbed = set(reg.specs) - {k for ks, _, _ in reg.jobs for k in ks}
    if unjobbed:
        print("ERROR: specs without a painter job:", sorted(unjobbed))
        return 1

    if not args.manifest_only:
        idxs = []
        for i, (ks, _, _) in enumerate(reg.jobs):
            if not args.only or any(k.startswith(p) for k in ks for p in args.only):
                idxs.append(i)
        # biggest jobs first for better load balancing
        weight = {"characters": 0, "creatures": 1, "environment": 2, "props": 3}
        idxs.sort(key=lambda i: weight.get(reg.jobs[i][1].__module__, 9))
        t0 = time.time()
        print("rendering %d jobs on %d processes..." % (len(idxs), args.jobs))
        if args.jobs > 1 and len(idxs) > 1:
            import multiprocessing as mp
            ctx = mp.get_context("fork") if hasattr(os, "fork") else mp.get_context()
            with ctx.Pool(args.jobs) as pool:
                for keys, out, dt in pool.imap_unordered(_run_job, idxs):
                    print("  %-34s %6.1fs  %s" % (keys[0] + ("" if len(keys) == 1 else " +%d" % (len(keys) - 1)),
                                                  dt, " ".join("%dk" % (b // 1024) for _, b in out)))
        else:
            for i in idxs:
                keys, out, dt = _run_job(i)
                print("  %-34s %6.1fs  %s" % (keys[0], dt, " ".join("%dk" % (b // 1024) for _, b in out)))
        print("rendered in %.1fs" % (time.time() - t0))

    order = [k for k in required] + [k for k in reg.specs if k not in set(required)]
    entries, problems, total = write_manifest(reg, order)
    cats = {}
    for e in entries:
        cats[e["category"]] = cats.get(e["category"], 0) + 1
    print("manifest: %d entries  %s" % (len(entries), ", ".join("%s %d" % kv for kv in sorted(cats.items()))))
    print("Resources/Art total: %.2f MB" % (total / 1048576.0))
    if problems:
        print("PROBLEMS:")
        for p in problems:
            print("   ", p)
        return 2
    print("all %d documented keys present; checks OK" % len(required))
    return 0


if __name__ == "__main__":
    sys.exit(main())
