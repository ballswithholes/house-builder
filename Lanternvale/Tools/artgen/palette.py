"""Lanternvale shared palette and colour helpers.

All colours are float RGB tuples in 0..1. The palette is deliberately small and pastel so every
generated asset sits in the same cosy, watercolour world:

* warm cream highlights, honey and terracotta accents (light, hearth, roofs)
* sage / moss greens (vegetation)
* dusty blues and violets for distance and shadow (atmospheric perspective)
* a warm dark violet-brown "ink" used for outlines instead of black
"""
import colorsys

import numpy as np


def hx(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


# --- core tones ---------------------------------------------------------------------------
INK = hx("3a2f42")           # outline / deepest shadow (warm violet-brown)
INK_SOFT = hx("5a4a5e")
CREAM = hx("fff4dc")
CREAM_WARM = hx("ffe6b3")
PAPER = hx("f6ecd6")
WHITE_WARM = hx("fffaf0")

# light & hearth
HONEY = hx("e8b04f")
AMBER = hx("f0a040")
LANTERN = hx("ffd77a")
LANTERN_CORE = hx("fff6d8")
EMBER = hx("e8743b")

# earth & accents
TERRACOTTA = hx("c9785a")
TERRACOTTA_DK = hx("a35a43")
ROSE = hx("e3a3a0")
BLUSH = hx("f0b4a8")
VERMILION = hx("d9573b")
BRICK = hx("b0603f")

# greens
GRASS_LIGHT = hx("c4d68e")
SAGE = hx("a8bc8a")
MOSS = hx("7d9a55")
MOSS_DK = hx("56763f")
FOREST = hx("3f6146")
FOREST_DK = hx("2c4636")
LEAF_YELLOW = hx("d8c96a")

# blues / violets (distance, water, shadow)
SKY_TOP = hx("8fb6d8")
SKY_MID = hx("bcd3e6")
SKY_HORIZON = hx("f6e4c8")
DUSTY_BLUE = hx("8fa7c4")
DUSTY_BLUE_DK = hx("62789b")
MIST = hx("dde3ee")
VIOLET_FAR = hx("a9a1c8")
VIOLET = hx("8a7bb0")
LAVENDER = hx("c6b8dc")
TEAL = hx("4f8f8c")

# wood & stone
WOOD = hx("8a5f3f")
WOOD_DK = hx("5e3f2c")
WOOD_LIGHT = hx("b88a5e")
THATCH = hx("d4a95c")
THATCH_DK = hx("a87c3c")
STONE = hx("a3a49c")
STONE_WARM = hx("b9b09d")
STONE_DK = hx("6f6f72")
PLASTER = hx("f1e3c6")
ROOF_BLUE = hx("6f8fb6")

# blight (the Hollow)
BLIGHT = hx("8e8899")
BLIGHT_DK = hx("5d566b")
BLIGHT_VIOLET = hx("9b7ad0")
BLIGHT_GLOW = hx("c9a8ff")

# skin tones
SKIN_FAIR = hx("f7d9c4")
SKIN_LIGHT = hx("efc7a8")
SKIN_TAN = hx("d7a07a")
SKIN_BROWN = hx("a86f4c")
SKIN_DEEP = hx("7d4f36")

# light direction used by every painter (screen space, y down): upper-left key light
LIGHT_DIR = np.array([-0.55, -0.70, 0.62], dtype=np.float32)
LIGHT_DIR = LIGHT_DIR / np.linalg.norm(LIGHT_DIR)


def arr(c):
    return np.asarray(c, dtype=np.float32)


def mix(a, b, t):
    a = np.asarray(a, dtype=np.float32)
    b = np.asarray(b, dtype=np.float32)
    return tuple(float(v) for v in (a + (b - a) * t))


def to_hsv(c):
    return colorsys.rgb_to_hsv(*[min(1.0, max(0.0, v)) for v in c])


def from_hsv(h, s, v):
    return colorsys.hsv_to_rgb(h % 1.0, min(1.0, max(0.0, s)), min(1.0, max(0.0, v)))


def shift(c, dh=0.0, ds=0.0, dv=0.0):
    h, s, v = to_hsv(c)
    return from_hsv(h + dh, s + ds, v + dv)


def scale_v(c, k):
    h, s, v = to_hsv(c)
    return from_hsv(h, s, v * k)


def shadow_of(c, k=1.0):
    """Painterly shadow: darker, a touch more saturated, hue pulled toward dusty violet."""
    dk = mix(c, INK, 0.18 * k)
    dk = mix(dk, VIOLET, 0.16 * k)
    h, s, v = to_hsv(dk)
    return from_hsv(h, s * (1.0 + 0.10 * k), v * (1.0 - 0.22 * k))


def light_of(c, k=1.0):
    """Painterly light: warmer and brighter, pulled toward cream."""
    lt = mix(c, CREAM, 0.30 * k)
    h, s, v = to_hsv(lt)
    return from_hsv(h, s * (1.0 - 0.05 * k), min(1.0, v * (1.0 + 0.10 * k)))


def line_of(c, k=1.0):
    """Line colour a little darker than the fill (clean, soft outlines)."""
    d = mix(c, INK, 0.55 * k)
    h, s, v = to_hsv(d)
    return from_hsv(h, s * 1.05, v * (1.0 - 0.10 * k))


def atmos(c, t, haze=MIST):
    """Atmospheric perspective: blend toward the haze colour and desaturate."""
    m = mix(c, haze, t)
    h, s, v = to_hsv(m)
    return from_hsv(h, s * (1.0 - 0.35 * t), v)


# --- class colour identities -------------------------------------------------------------
CLASS_COLORS = {
    "warrior": hx("c0392b"),
    "hunter": hx("6f9a3c"),
    "paladin": hx("e6b84a"),
    "mage": hx("6d5aa8"),
    "priest": hx("8fb8de"),
    "rogue": hx("e88a3a"),
    "warlock": hx("7a4fb0"),
    "shaman": hx("3f78b5"),
}
