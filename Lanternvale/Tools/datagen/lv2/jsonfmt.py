"""Compact-but-readable JSON in the style of the hand-written Lanternvale data files: an object or list that fits in
the line budget is written on one line, anything longer is expanded one member per line."""
import json

WIDTH = 150


def _r(v):
    if isinstance(v, float):
        r = round(v, 3)
        return int(r) if r == int(r) and abs(r) < 1e6 and False else r
    if isinstance(v, dict):
        return {k: _r(x) for k, x in v.items()}
    if isinstance(v, list):
        return [_r(x) for x in v]
    return v


def _inline(v):
    return json.dumps(v, ensure_ascii=False, separators=(", ", ": "))


def dumps(v, ind=0):
    v = _r(v)
    return _dump(v, ind)


def _dump(v, ind):
    s = _inline(v)
    if not isinstance(v, (dict, list)) or len(s) + ind <= WIDTH:
        return s
    pad = " " * (ind + 2)
    if isinstance(v, dict):
        items = [pad + json.dumps(k, ensure_ascii=False) + ": " + _dump(x, ind + 2 + len(k) + 4 - (len(k) + 4)) for k, x in v.items()]
        return "{\n" + ",\n".join(items) + "\n" + " " * ind + "}"
    items = [pad + _dump(x, ind + 2) for x in v]
    return "[\n" + ",\n".join(items) + "\n" + " " * ind + "]"


def write(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        f.write(dumps(obj) + "\n")
