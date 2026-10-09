"""UI art: tileable parchment, screen vignette and the hand-lettered title logo."""
import math

import numpy as np
from PIL import Image

import palette as P
from brushes import (Canvas, F32, blob, blur, catmull, colfield, downsample, drop_shadow, ellipse, flat_fill, glow, grain, outline,
                     paint, ragged, rng, smoothstep, to_image, wash)


def ui_parchment(key="ui_parchment"):
    cv = Canvas(512, 512, 1.0, pad=96, wrap_x=True, wrap_y=True, seed=key)
    full = np.ones((cv.PH, cv.PW), F32)
    fld = colfield(P.hx("f6ead0"), P.hx("ead6b0"), smoothstep(0.3, 0.75, cv.noise(180, "tone", 4)))
    wash(cv, full, P.PAPER, 1.0, field=fld, pool=0, gran=0.05, var=0.03, var_cell=40, key="base")
    # soft stains with darker rims (tileable)
    st = smoothstep(0.6, 0.66, cv.noise(120, "stain", 4))
    wash(cv, st.astype(F32), P.hx("dcc49a"), 0.28, pool=0.6, pool_w=5, gran=0.1, key="st")
    # fibres
    fib = cv.noise((2.5, 60), "fib", 3)
    cv.multiply(1.0 + (fib - 0.5) * 0.06)
    fib2 = cv.noise((60, 2.5), "fib2", 3)
    cv.multiply(1.0 + (fib2 - 0.5) * 0.04)
    grain(cv, 0.035, cell=1.3, key="g")
    rgb, a = cv.crop_pad()
    return to_image(rgb, a, do_grade=False)


def ui_vignette(key="ui_vignette"):
    n = 1024
    y, x = np.mgrid[0:n, 0:n].astype(F32)
    nx = (x + 0.5) / n * 2 - 1
    ny = (y + 0.5) / n * 2 - 1
    d = np.sqrt((nx * 0.92) ** 2 + ny ** 2)
    a = smoothstep(0.55, 1.45, d) ** 1.3 * 0.75
    col = np.asarray(P.hx("231b2c"), F32)
    rgb = np.broadcast_to(col, (n, n, 3))
    out = np.dstack([np.round(rgb * 255), np.round(a * 255)]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


# --- hand-lettered logo -------------------------------------------------------------------
# letters as brush strokes in units: x right, y up from the baseline (x-height = 1)
LETTERS = {
    "L": (0.85, [[(0.05, 1.5), (0.12, 1.0), (0.1, 0.45), (0.05, 0.02)], [(0.05, 0.02), (0.4, 0.06), (0.82, 0.0)],
                 [(-0.12, 1.36), (0.05, 1.52), (0.25, 1.46)]]),
    "a": (0.95, [[(0.82, 0.78), (0.55, 0.98), (0.2, 0.86), (0.05, 0.45), (0.25, 0.04), (0.6, 0.12), (0.82, 0.5)],
                 [(0.84, 0.95), (0.84, 0.4), (0.86, 0.1), (0.98, 0.0)]]),
    "n": (0.92, [[(0.06, 0.98), (0.06, 0.5), (0.06, 0.0)], [(0.06, 0.6), (0.3, 0.95), (0.62, 0.95), (0.8, 0.6), (0.82, 0.25), (0.86, 0.0)]]),
    "t": (0.78, [[(0.3, 1.4), (0.3, 0.8), (0.32, 0.2), (0.5, 0.0), (0.72, 0.1)], [(0.0, 0.94), (0.4, 0.98), (0.74, 0.94)]]),
    "e": (0.9, [[(0.08, 0.5), (0.5, 0.52), (0.85, 0.56), (0.7, 0.92), (0.35, 0.95), (0.06, 0.6), (0.2, 0.1), (0.55, 0.0), (0.86, 0.18)]]),
    "r": (0.7, [[(0.06, 0.98), (0.06, 0.5), (0.06, 0.0)], [(0.06, 0.6), (0.28, 0.92), (0.55, 0.98), (0.72, 0.86)]]),
    "v": (0.92, [[(0.0, 0.98), (0.2, 0.5), (0.44, 0.0)], [(0.44, 0.0), (0.7, 0.55), (0.92, 1.0)]]),
    "l": (0.48, [[(0.18, 1.52), (0.18, 0.8), (0.2, 0.2), (0.36, 0.0), (0.5, 0.06)], [(0.18, 1.5), (0.4, 1.86), (0.95, 1.95), (1.42, 1.8), (1.56, 1.62)]]),
}


def logo_lanternvale(key="logo_lanternvale"):
    W, H = 1024, 512
    S = 2
    cv = Canvas(W, H, S, seed=key)
    r = rng(key)
    word = "Lanternvale"
    spacing = 0.12
    total = sum(LETTERS[c][0] for c in word) + spacing * (len(word) - 1)
    u = 820.0 / total
    x = (W - total * u) / 2
    base = 330.0
    strokes = []
    tbar = None
    for i, ch in enumerate(word):
        w, ss = LETTERS[ch]
        bounce = math.sin(i * 1.3) * 7
        rot = math.sin(i * 2.1) * 0.04
        for st in ss:
            pts = []
            for (px, py) in st:
                X = x + px * u
                Y = base - py * u + bounce
                # slight per-letter rotation about the letter base
                cx0 = x + w * u / 2
                dx, dy = X - cx0, Y - base
                X, Y = cx0 + dx * math.cos(rot) - dy * math.sin(rot), base + dx * math.sin(rot) + dy * math.cos(rot)
                pts.append((X, Y))
            strokes.append(pts)
        if ch == "l":
            tbar = (x + 1.56 * u, base - 1.62 * u + bounce)
        x += (w + spacing) * u
    from characters import stroke_w
    polys = []
    for pts in strokes:
        n = len(pts)
        ws = [u * (0.2 + 0.08 * math.sin(math.pi * k / max(1, n - 1))) for k in range(n)]
        ws[0] *= 0.7
        ws[-1] *= 0.55
        polys.append(stroke_w(pts, ws, n=10))
    m = cv.polys_mask(polys)
    # soft warm glow behind the letters
    gl = cv.blur(m, 22)
    cv.over(P.LANTERN, np.clip(gl * 0.55, 0, 1))
    # thick ink outline + letters with a cream -> honey gradient
    dil = smoothstep(0.03, 0.2, cv.blur(m, 6))
    cv.over(P.hx("3a2440"), dil)
    grad = smoothstep(base - u * 1.4, base + 10, cv.yy())
    fld = colfield(P.hx("fff7e2"), P.hx("f0b04a"), np.broadcast_to(grad, m.shape))
    from brushes import bbox
    ys, xs = bbox(m, 2)
    cv.over(fld[ys, xs], m[ys, xs], (ys, xs))
    # inner highlight along the upper edge, shadow at the lower edge
    hi = np.clip(m - np.roll(m, 6 * S, 0), 0, 1)
    cv.atop(P.WHITE_WARM, cv.blur(hi, 2) * 0.8)
    lo = np.clip(m - np.roll(m, -6 * S, 0), 0, 1)
    cv.atop(P.TERRACOTTA_DK, cv.blur(lo, 2) * 0.5)
    # little paper lantern hanging from the t's crossbar
    if tbar is not None:
        lx, ly = tbar
        from props import hanging_lantern
        sub = Canvas(W, H, S, seed=key + "lan")
        cv.over(P.hx("3a2440"), cv.mask(np.array([(lx - 3, ly + 4), (lx + 3, ly + 4), (lx + 3, ly + 22), (lx - 3, ly + 22)])))
        ly = ly - 16
        body = catmull(np.array([(lx, ly + 38), (lx + 28, ly + 54), (lx + 31, ly + 88), (lx, ly + 106), (lx - 31, ly + 88), (lx - 28, ly + 54)]), True, 6)
        bm = cv.mask(body)
        dil2 = smoothstep(0.03, 0.2, cv.blur(bm, 5))
        cv.over(P.hx("3a2440"), dil2)
        from props import glow_pane
        glow_pane(cv, bm)
        for k in (0.35, 0.6, 0.85):
            yy = ly + 38 + 68 * k
            cv.atop(P.VERMILION, cv.lines_mask([(lx - 40, yy, lx + 40, yy)], 3) * bm * 0.6)
        for yy in (ly + 36, ly + 105):
            cv.over(P.hx("3a2440"), cv.mask(np.array([(lx - 16, yy - 5), (lx + 16, yy - 5), (lx + 16, yy + 5), (lx - 16, yy + 5)])))
        glow(cv, lx, ly + 72, 150, P.LANTERN, 0.45, clip=False, falloff=2.2)
    # sprig of leaves curling from the L + sparkles
    from environment import leaf_poly
    lx0 = (W - total * u) / 2
    vine = [(lx0 - 10, base + 30), (lx0 + 60, base + 60), (lx0 + 200, base + 70), (lx0 + 330, base + 55)]
    vm = cv.mask(stroke_w(vine, [6, 5, 4, 1.5], n=10))
    cv.over(P.hx("3a2440"), smoothstep(0.03, 0.2, cv.blur(vm, 3)))
    cv.over(P.MOSS, vm)
    for k in range(6):
        t = (k + 0.5) / 6
        c = catmull(np.array(vine), closed=False, n=10)
        p = c[int(t * (len(c) - 1))]
        lp = leaf_poly(p[0], p[1], 34 - k * 3, (-0.9 if k % 2 else 0.7), 0.5)
        lm = cv.mask(lp)
        cv.over(P.hx("3a2440"), smoothstep(0.03, 0.2, cv.blur(lm, 3)))
        paint(cv, lm, P.mix(P.MOSS, P.GRASS_LIGHT, 0.4 * (k % 2)), line=0.0, soft=4, ao=0, hi=0.5, cel=0.6)
    for k in range(9):
        sx, sy = 80 + r.random() * 860, 90 + r.random() * 330
        if cv.a[int(sy * S), int(sx * S)] > 0.3:
            continue
        glow(cv, sx, sy, 10 + r.random() * 8, P.LANTERN_CORE, 1.0, clip=False, falloff=1.2)
        glow(cv, sx, sy, 34, P.LANTERN, 0.35, clip=False)
    rgb, a = downsample(cv.rgb, cv.a, S)
    return to_image(rgb, a, bg_rgb=P.hx("3a2440"), do_grade=False)


def _one(fn, key):
    return {key: fn(key)}


def register(reg):
    reg.spec("ui_parchment", "UI", size=(512, 512), height=1.0, pivot=(0.5, 0.5), tile_y=True, loop=False)
    reg.job(["ui_parchment"], _one, ui_parchment, "ui_parchment")
    reg.spec("ui_vignette", "UI", size=(1024, 1024), height=1.0, pivot=(0.5, 0.5))
    reg.job(["ui_vignette"], _one, ui_vignette, "ui_vignette")
    reg.spec("logo_lanternvale", "UI", size=(1024, 512), height=1.0, pivot=(0.5, 0.5))
    reg.job(["logo_lanternvale"], _one, logo_lanternvale, "logo_lanternvale")
