"""Midground props: cosy cottages, inn, windmill, trees, lanterns, shrine pieces and village clutter.

Style: front-facing orthographic with a slight oblique depth (right side faces recede up-right), key light
from the upper left, clean dark outline, painterly fills. Windows and lanterns carry a bright warm
emissive core so 2D lights in the engine can add glow on top.
"""
import math

import numpy as np

import palette as P
from brushes import (Canvas, F32, blob, blur, catmull, colfield, drop_shadow, ellipse, flat_fill, glow, grain,
                     outline, paint, ragged, rim_light, rng, rotate, smoothstep, stroke, taper, to_image, warp, wash)
from environment import fern, grass_tuft, leaf_poly, mossy_stone, round_tree


# ----------------------------------------------------------------------------------------
# helpers
# ----------------------------------------------------------------------------------------
def new(key, w, h, scale=1.0):
    return Canvas(w, h, scale, seed=key)


def done(cv, ow=2.6, rim=0.32, grain_amt=0.028, rim_w=3.0):
    rim_light(cv, rim, rim_w)
    grain(cv, grain_amt, key="paper")
    outline(cv, ow)
    return to_image(cv.rgb, cv.a)


def A(pts):
    return np.asarray(pts, dtype=np.float64)


def part(cv, pts, col, smooth=False, n=8, **kw):
    pts = A(pts)
    if smooth:
        pts = catmull(pts, True, n)
    m = cv.mask(pts)
    paint(cv, m, col, **kw)
    return m


def ell(cv, cx, cy, rx, ry, col, rot=0.0, **kw):
    m = cv.ellipse_mask(cx, cy, rx, ry, rot)
    paint(cv, m, col, **kw)
    return m


def sline(cv, pts, w0, col, w1=None, **kw):
    m = cv.mask(stroke(A(pts), w0, w1 if w1 is not None else w0, n=6))
    kw.setdefault("soft", max(1.0, w0 * 0.4))
    kw.setdefault("ao", 0)
    paint(cv, m, col, **kw)
    return m


def wood_tex(cv, key, vertical=False, cell=40):
    n = cv.noise((cell, 4) if vertical else (4, cell), key + "wood", 3)
    return 0.5 + (n - 0.5) * 0.45


def lines(cv, segs, width, col, alpha=0.6, clip=None):
    m = cv.lines_mask(segs, width)
    if clip is not None:
        m = m * clip
    cv.atop(col, m * alpha)


class Proj:
    """Oblique projection: x right, y up, z depth (away). k pixels per metre, origin = ground front centre."""

    def __init__(self, ox, oy, k, dx=0.42, dy=0.26):
        self.ox, self.oy, self.k, self.dx, self.dy = ox, oy, k, dx, dy

    def __call__(self, x, y, z=0.0):
        return (self.ox + (x + z * self.dx) * self.k, self.oy - (y + z * self.dy) * self.k)

    def poly(self, pts3):
        return A([self(*p) for p in pts3])


def glow_pane(cv, mask, strength=1.0):
    """Emissive window/lantern fill: hot cream centre fading to honey."""
    from brushes import bbox
    bb = bbox(mask, 2)
    if bb is None:
        return
    ys, xs = bb
    m = mask[ys, xs]
    yy, xx = np.mgrid[0:m.shape[0], 0:m.shape[1]].astype(F32)
    w = m.sum()
    cy = (yy * m).sum() / max(w, 1e-6)
    cx = (xx * m).sum() / max(w, 1e-6)
    rad = math.sqrt(max(w, 1.0) / math.pi) * 1.3
    d = np.clip(np.hypot(yy - cy, xx - cx) / rad, 0, 1)
    col = colfield(P.LANTERN_CORE, P.mix(P.LANTERN, P.AMBER, 0.45), d ** 1.2)
    cv.over(col, m, (ys, xs))


def window(cv, cx, cy, w, h, kind="rect", frame=P.WOOD, lit=True, mullion=True, sill=True, spill=True, box=None):
    """Front-facing window. kind: rect | round | arch."""
    fw = max(2.0, w * 0.12)
    if kind == "round":
        outer = ellipse(cx, cy, w / 2 + fw, h / 2 + fw, 0, 40)
        inner = ellipse(cx, cy, w / 2, h / 2, 0, 40)
    elif kind == "arch":
        top = ellipse(cx, cy - h / 2 + w / 2, w / 2, w / 2, 0, 24, math.pi, 2 * math.pi)
        inner = np.concatenate([A([(cx - w / 2, cy + h / 2)]), top, A([(cx + w / 2, cy + h / 2)])])
        topo = ellipse(cx, cy - h / 2 + w / 2, w / 2 + fw, w / 2 + fw, 0, 24, math.pi, 2 * math.pi)
        outer = np.concatenate([A([(cx - w / 2 - fw, cy + h / 2 + fw)]), topo, A([(cx + w / 2 + fw, cy + h / 2 + fw)])])
    else:
        outer = A([(cx - w / 2 - fw, cy - h / 2 - fw), (cx + w / 2 + fw, cy - h / 2 - fw), (cx + w / 2 + fw, cy + h / 2 + fw),
                   (cx - w / 2 - fw, cy + h / 2 + fw)])
        inner = A([(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h / 2), (cx - w / 2, cy + h / 2)])
    mo = cv.mask(outer)
    drop_shadow(cv, mo, 2, 3, 3, 0.3)
    paint(cv, mo, frame, line=0.8, line_w=1.2, soft=2, ao=0, tex=wood_tex(cv, "wf"))
    mi = cv.mask(inner)
    if lit:
        glow_pane(cv, mi)
    else:
        paint(cv, mi, P.mix(P.DUSTY_BLUE_DK, P.INK, 0.4), line=0.5, soft=3, ao=0, hi=0.6)
    if mullion:
        mw = max(1.5, w * 0.07)
        segs = [(cx, cy - h / 2, cx, cy + h / 2), (cx - w / 2, cy, cx + w / 2, cy)]
        m = cv.lines_mask(segs, mw) * mi
        paint(cv, m, P.mix(frame, P.INK, 0.25), line=0, soft=1, ao=0, flat=True)
    if sill and kind != "round":
        sl = A([(cx - w / 2 - fw * 1.8, cy + h / 2 + fw * 0.6), (cx + w / 2 + fw * 1.8, cy + h / 2 + fw * 0.6),
                (cx + w / 2 + fw * 1.5, cy + h / 2 + fw * 2.0), (cx - w / 2 - fw * 1.5, cy + h / 2 + fw * 2.0)])
        part(cv, sl, P.light_of(frame, 0.6), line=0.8, line_w=1.0, soft=2, ao=0)
    if lit and spill:
        glow(cv, cx, cy, max(w, h) * 1.6, P.LANTERN, 0.22, clip=True, falloff=1.6)
    if box is not None:
        flower_box(cv, cx, cy + h / 2 + fw * 2.0, w + fw * 3, box)
    return mo


def flower_box(cv, cx, top, w, flowers):
    h = w * 0.22
    bx = A([(cx - w / 2, top), (cx + w / 2, top), (cx + w / 2 - 2, top + h), (cx - w / 2 + 2, top + h)])
    r = rng("fb", cx, top)
    # leaves and blossoms spilling over
    polys = []
    for i in range(int(w / 5)):
        x = cx - w / 2 + r.random() * w
        polys.append(blob(x, top - r.random() * h * 0.6, h * 0.5, h * 0.35, r, 0.2, 6))
    m = cv.polys_mask(polys)
    paint(cv, m, P.MOSS, line=0.6, line_w=1.0, soft=2, ao=0.2, var=0.1, var_cell=6)
    for i in range(int(w / 7)):
        x = cx - w / 2 + 3 + r.random() * (w - 6)
        y = top - h * 0.2 - r.random() * h * 0.7
        c = flowers[i % len(flowers)]
        ell(cv, x, y, h * 0.22, h * 0.2, c, line=0.6, line_w=0.8, soft=1, ao=0, hi=0.5)
    part(cv, bx, P.TERRACOTTA, line=0.8, line_w=1.0, soft=3, tex=wood_tex(cv, "fb"))


def door(cv, cx, by, w, h, col=P.WOOD, arch=True, step=True, lantern=False):
    if arch:
        top = ellipse(cx, by - h + w / 2, w / 2, w / 2, 0, 24, math.pi, 2 * math.pi)
        pts = np.concatenate([A([(cx - w / 2, by)]), top, A([(cx + w / 2, by)])])
        fr_top = ellipse(cx, by - h + w / 2, w / 2 + 5, w / 2 + 5, 0, 24, math.pi, 2 * math.pi)
        frame = np.concatenate([A([(cx - w / 2 - 5, by)]), fr_top, A([(cx + w / 2 + 5, by)])])
    else:
        pts = A([(cx - w / 2, by), (cx - w / 2, by - h), (cx + w / 2, by - h), (cx + w / 2, by)])
        frame = A([(cx - w / 2 - 5, by), (cx - w / 2 - 5, by - h - 5), (cx + w / 2 + 5, by - h - 5), (cx + w / 2 + 5, by)])
    mf = cv.mask(frame)
    drop_shadow(cv, mf, 3, 3, 4, 0.35)
    paint(cv, mf, P.mix(P.STONE_WARM, P.WOOD_LIGHT, 0.4), line=0.8, line_w=1.2, soft=3, ao=0.1)
    md = cv.mask(pts)
    paint(cv, md, col, line=0.9, line_w=1.4, soft=4, ao=0.35, tex=wood_tex(cv, "door", vertical=True))
    segs = [(cx + k * w / 4, by - h, cx + k * w / 4, by) for k in (-1, 0, 1)]
    lines(cv, segs, 1.6, P.shadow_of(col, 1.4), 0.6, clip=md)
    # hinges + knob
    for yy in (by - h * 0.25, by - h * 0.65):
        sline(cv, [(cx - w / 2 + 2, yy), (cx - w * 0.05, yy)], max(2.5, h * 0.035), P.INK_SOFT, line=0.3)
    ell(cv, cx + w * 0.3, by - h * 0.45, max(2, w * 0.06), max(2, w * 0.06), P.HONEY, line=0.6, line_w=0.8, soft=1, ao=0)
    if step:
        st = A([(cx - w / 2 - 10, by - 2), (cx + w / 2 + 10, by - 2), (cx + w / 2 + 14, by + 8), (cx - w / 2 - 14, by + 8)])
        part(cv, st, P.STONE_WARM, line=0.8, line_w=1.0, soft=3, ao=0.1)
    if lantern:
        hanging_lantern(cv, cx + w / 2 + 22, by - h * 0.95, max(10, w * 0.22))
    return md


def hanging_lantern(cv, x, y, s, paper=False):
    """Small iron/paper lantern hanging from a bracket at (x, y)."""
    sline(cv, [(x - s * 1.2, y - s * 0.2), (x, y - s * 0.2)], max(2, s * 0.15), P.INK_SOFT, line=0.3)
    sline(cv, [(x, y - s * 0.2), (x, y + s * 0.3)], max(1.2, s * 0.08), P.INK_SOFT, line=0)
    if paper:
        m = cv.mask(catmull(A([(x, y + s * 0.3), (x + s * 0.55, y + s * 0.6), (x + s * 0.6, y + s * 1.3), (x, y + s * 1.7),
                               (x - s * 0.6, y + s * 1.3), (x - s * 0.55, y + s * 0.6)]), True, 6))
        glow_pane(cv, m)
        paint(cv, m, P.LANTERN, line=0.7, line_w=1.0, soft=3, opacity=0.25, ao=0, hi=0)
        for k in (0.75, 1.05, 1.35):
            lines(cv, [(x - s * 0.6, y + s * k, x + s * 0.6, y + s * k)], max(1, s * 0.05), P.VERMILION, 0.5, clip=m)
        part(cv, [(x - s * 0.3, y + s * 0.25), (x + s * 0.3, y + s * 0.25), (x + s * 0.25, y + s * 0.4), (x - s * 0.25, y + s * 0.4)],
             P.INK_SOFT, line=0.3, soft=1, ao=0)
        part(cv, [(x - s * 0.25, y + s * 1.65), (x + s * 0.25, y + s * 1.65), (x + s * 0.3, y + s * 1.8), (x - s * 0.3, y + s * 1.8)],
             P.INK_SOFT, line=0.3, soft=1, ao=0)
    else:
        body = A([(x - s * 0.4, y + s * 0.5), (x + s * 0.4, y + s * 0.5), (x + s * 0.5, y + s * 1.4), (x - s * 0.5, y + s * 1.4)])
        glow_pane(cv, cv.mask(body))
        cap = A([(x - s * 0.55, y + s * 0.55), (x, y + s * 0.2), (x + s * 0.55, y + s * 0.55)])
        part(cv, cap, P.INK_SOFT, line=0.4, soft=1, ao=0)
        lines(cv, [(x - s * 0.45, y + s * 0.5, x - s * 0.5, y + s * 1.4), (x + s * 0.45, y + s * 0.5, x + s * 0.5, y + s * 1.4),
                   (x, y + s * 0.5, x, y + s * 1.4), (x - s * 0.5, y + s * 1.4, x + s * 0.5, y + s * 1.4)], max(1.2, s * 0.1),
              P.INK_SOFT, 0.9)
    glow(cv, x, y + s, s * 3.2, P.LANTERN, 0.3, clip=True)


def stone_band(cv, x0, x1, y0, y1, r, key, col=P.STONE_WARM, size=None):
    size = size or (y1 - y0) * 0.55
    polys = []
    y = y0
    row = 0
    while y < y1 - 2:
        x = x0 - (row % 2) * size * 0.5
        hh = min(size * (0.8 + r.random() * 0.4), y1 - y)
        while x < x1:
            w = size * (1.0 + r.random() * 0.8)
            xa, xb = max(x0, x + 1.5), min(x1, x + w - 1.5)
            if xb - xa > 3:
                polys.append(catmull(A([(xa, y + 1.5), (xb, y + 1.5), (xb, y + hh - 1.5), (xa, y + hh - 1.5)]), True, 4))
            x += w
        y += hh
        row += 1
    back = A([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])
    part(cv, back, P.shadow_of(col, 1.3), line=0, soft=2, ao=0, flat=True)
    m = cv.polys_mask(polys)
    paint(cv, m, col, line=0.7, line_w=1.0, soft=size * 0.25, key=key, var=0.07, var_cell=size * 1.5, hi=0.4, ao=0.1)
    # gentle per-stone tint variation
    r2 = rng(key, "tint")
    for i in range(len(polys) // 4):
        p = polys[r2.integers(len(polys))]
        cv.atop(P.mix(col, [P.TERRACOTTA, P.LAVENDER, P.CREAM_WARM][i % 3], 0.5), cv.mask(p) * 0.25)
    return m


def plaster(cv, pts, col, key, shade=0.5, ao=0.25):
    m = cv.mask(A(pts))
    tex = cv.noise(70, key + "pl", 3)
    paint(cv, m, col, line=0.7, line_w=1.2, soft=12, ao=ao, shade=shade, tex=tex * 0.16 + 0.42, var=0.025, var_cell=60,
          key=key)
    # worn patches showing bricks
    r = rng(key, "patch")
    return m


def thatch(cv, outline_pts, key, col=P.THATCH, down=(0.0, 1.0), rows=6, side_pts=None, bulge=0.06):
    """Puffy thatched roof from an outline polygon; straw strokes run along `down`."""
    pts = A(outline_pts)
    c = pts.mean(0)
    puffy = []
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        puffy.append(a)
        mid = (a + b) / 2
        out = mid - c
        L = np.linalg.norm(b - a)
        puffy.append(mid + out / max(np.linalg.norm(out), 1e-6) * L * bulge)
    shape = catmull(A(puffy), True, 8)
    m = cv.mask(shape)
    m = warp(cv, m, 2.0, 18, key + "w")
    drop_shadow(cv, m, 4, 9, 7, 0.45)
    paint(cv, m, col, line=0.8, line_w=1.6, soft=20, key=key, ao=0.2, hi=0.35, var=0.1, var_cell=26, cel=0.4)
    if side_pts is not None:
        sm = cv.mask(A(side_pts)) * m
        cv.atop(P.shadow_of(col, 1.0), sm * 0.55)
    # straw strokes
    r = rng(key, "straw")
    from brushes import bbox
    bb = bbox(m)
    ys, xs = bb
    x0, x1 = (xs.start - cv.padx) / cv.s, (xs.stop - cv.padx) / cv.s
    y0, y1 = (ys.start - cv.pady) / cv.s, (ys.stop - cv.pady) / cv.s
    dx, dy = down
    nrm = math.hypot(dx, dy)
    dx, dy = dx / nrm, dy / nrm
    segs_d, segs_l = [], []
    n = int((x1 - x0) * (y1 - y0) / 55)
    for i in range(n):
        x = x0 + r.random() * (x1 - x0)
        y = y0 + r.random() * (y1 - y0)
        L = 8 + r.random() * 14
        jx = r.normal() * 0.15
        seg = (x, y, x + (dx + jx) * L, y + dy * L)
        (segs_d if r.random() < 0.55 else segs_l).append(seg)
    lines(cv, segs_d, 1.3, P.shadow_of(col, 1.1), 0.45, clip=m)
    lines(cv, segs_l, 1.1, P.light_of(col, 1.2), 0.45, clip=m)
    # layered rows: darker bands under each thatch course
    if rows:
        rowmask = np.zeros_like(m)
        for k in range(1, rows):
            t = k / rows
            yy = y0 + (y1 - y0) * t
            band = np.exp(-((cv.yy() - yy - (cv.xx() - x0) * (-dy * 0 + 0)) / 3.0) ** 2)
            rowmask = np.maximum(rowmask, np.broadcast_to(band, m.shape))
        cv.atop(P.shadow_of(col, 1.2), rowmask * m * 0.3)
    return m


def tile_roof(cv, pts, key, col=P.ROOF_BLUE, rows=7, tile_w=18, side_pts=None):
    """Scalloped tile roof inside polygon pts (front slope), rows from top to bottom."""
    m = cv.mask(A(pts))
    drop_shadow(cv, m, 4, 9, 7, 0.45)
    paint(cv, m, P.shadow_of(col, 0.8), line=0.8, line_w=1.5, soft=12, key=key, ao=0.1)
    from brushes import bbox
    ys, xs = bbox(m)
    x0, x1 = (xs.start - cv.padx) / cv.s, (xs.stop - cv.padx) / cv.s
    y0, y1 = (ys.start - cv.pady) / cv.s, (ys.stop - cv.pady) / cv.s
    rh = (y1 - y0) / rows
    r = rng(key, "tiles")
    for k in range(rows):
        yy = y0 + k * rh
        polys = []
        off = (k % 2) * tile_w / 2
        x = x0 - tile_w + off
        while x < x1 + tile_w:
            polys.append(catmull(A([(x, yy), (x + tile_w, yy), (x + tile_w, yy + rh * 0.9), (x + tile_w * 0.5, yy + rh * 1.15),
                                    (x, yy + rh * 0.9)]), True, 5))
            x += tile_w
        tm = cv.polys_mask(polys) * m
        cv_col = P.mix(col, P.CREAM, 0.06 * (rows - k) / rows)
        paint(cv, tm, cv_col, line=0.7, line_w=1.0, soft=3, key=key + str(k), ao=0.3, var=0.1, var_cell=12, hi=0.35)
    if side_pts is not None:
        sm = cv.mask(A(side_pts)) * m
        cv.atop(P.shadow_of(col, 1.0), sm * 0.5)
    return m


def ivy(cv, pts, r, key, size=6, n=40, col=P.MOSS):
    c = catmull(A(pts), closed=False, n=10)
    vine = cv.mask(stroke(A(pts), max(2.0, size * 0.25), max(1.0, size * 0.12)))
    paint(cv, vine, P.WOOD_DK, line=0.3, soft=1, ao=0)
    polys = []
    n = max(6, n // 3)
    for i in range(n):
        p = c[int((i + r.random()) / n * (len(c) - 1))]
        s = size * 1.6 * (0.8 + r.random() * 0.5)
        side = 1 if i % 2 else -1
        a = -math.pi / 2 + side * (0.6 + r.random() * 0.6)
        polys.append(leaf_poly(p[0], p[1], s, a, 0.75))
    m = cv.polys_mask(polys)
    drop_shadow(cv, m, 2, 3, 2, 0.3)
    paint(cv, m, col, line=0.7, line_w=0.9, soft=3, key=key, ao=0.1, var=0.08, var_cell=12, hi=0.5, cel=0.55)


def chimney(cv, x, base_y, w, h, r, key, col=P.STONE_WARM, crooked=0.0):
    pts = A([(x - w / 2, base_y), (x - w / 2 + crooked * 0.5, base_y - h * 0.6), (x - w / 2 + crooked, base_y - h),
             (x + w / 2 + crooked, base_y - h), (x + w / 2 + crooked * 0.5, base_y - h * 0.6), (x + w / 2, base_y)])
    m = cv.mask(pts)
    drop_shadow(cv, m, 4, 4, 5, 0.35)
    paint(cv, m, P.shadow_of(col, 0.7), line=0.8, line_w=1.2, soft=4, ao=0)
    # stones
    polys = []
    yy = base_y - 3
    k = 0
    while yy > base_y - h + 4:
        t = (base_y - yy) / h
        cx = x + crooked * t
        sw = w / 2
        off = (k % 2) * sw / 2
        xx = cx - w / 2 - off
        while xx < cx + w / 2:
            polys.append(blob(xx + sw / 2, yy - 4, sw * 0.46, 4.5, r, 0.12, 6))
            xx += sw
        yy -= 9
        k += 1
    sm = cv.polys_mask(polys) * m
    paint(cv, sm, col, line=0.6, line_w=0.8, soft=2, key=key, var=0.15, var_cell=6, hi=0.4, ao=0)
    cap = A([(x - w / 2 - 3 + crooked, base_y - h - 2), (x + w / 2 + 3 + crooked, base_y - h - 2), (x + w / 2 + 3 + crooked, base_y - h + 6),
             (x - w / 2 - 3 + crooked, base_y - h + 6)])
    part(cv, cap, P.STONE, line=0.8, line_w=1.0, soft=2, ao=0)
    return m


def moss_on(cv, key, cell=30, thr=0.6, opacity=1.0, where=None, top_bias=None):
    """Soft moss patches on already-painted areas (bigger, smoother than speckles)."""
    n = cv.noise(cell, key + "moss", 3)
    if top_bias is not None:
        n = n + top_bias
    mo = cv.a * smoothstep(thr, thr + 0.06, n) * (cv.a > 0.95)
    if where is not None:
        mo = mo * where
    mo = ragged(cv, mo.astype(F32), 0.35, 1.2, max(3, cell * 0.15), key + "mr")
    paint(cv, mo, P.MOSS, line=0.45, line_w=0.9, soft=4, var=0.06, var_cell=cell * 0.4, hi=0.5, ao=0.15, opacity=opacity,
          light_col=P.GRASS_LIGHT)


def shadow_ellipse(cv, cx, cy, rx, ry, strength=0.35):
    m = cv.blur(cv.ellipse_mask(cx, cy, rx, ry), ry * 0.6)
    flat_fill(cv, m, P.mix(P.INK, P.VIOLET, 0.45), strength * 0.6)


def wall_shade(c):
    """Side wall colour: cooler and darker but still warm (no grey mud)."""
    return P.mix(P.scale_v(c, 0.8), P.LAVENDER, 0.25)


def grass_base(cv, x0, x1, y, r, key, h=26, n=4):
    for i in range(n):
        x = x0 + (i + r.random()) / n * (x1 - x0)
        grass_tuft(cv, x, y + 2, h * (0.7 + r.random() * 0.6), h * 2.2, r, key + str(i), n=12)


# ----------------------------------------------------------------------------------------
# buildings
# ----------------------------------------------------------------------------------------
FLOWERS = [P.ROSE, P.hx("f6cf4a"), P.WHITE_WARM, P.hx("ec6d5a"), P.LAVENDER]


def prop_cottage_a(key="prop_cottage_a"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    pj = Proj(470, 960, 118)
    W, H, D = 5.0, 2.5, 3.4
    x0, x1 = -W / 2, W / 2
    shadow_ellipse(cv, 540, 962, 400, 34)
    front = pj.poly([(x0, 0, 0), (x1, 0, 0), (x1, H, 0), (x0, H, 0)])
    side = pj.poly([(x1, 0, 0), (x1, 0, D), (x1, H, D), (x1, H, 0)])
    gable = pj.poly([(x1, H, 0), (x1, H, D), (x1, H + 2.2, D / 2)])
    plaster(cv, side, wall_shade(P.PLASTER), key + "s", shade=0.2)
    plaster(cv, gable, P.scale_v(wall_shade(P.PLASTER), 1.05), key + "g", shade=0.2)
    plaster(cv, front, P.PLASTER, key + "f")
    # timber frame
    tf = []
    for xx in (x0 + 0.05, x0 + W * 0.36, x1 - 0.05):
        tf.append(stroke(A([pj(xx, 0.35), pj(xx, H)]), 11, 11, n=2, cap=False))
    tf.append(stroke(A([pj(x0, H - 0.08), pj(x1, H - 0.08)]), 12, 12, n=2, cap=False))
    tf.append(stroke(A([pj(x0, 1.3), pj(x0 + W * 0.36, 1.3)]), 9, 9, n=2, cap=False))
    tf.append(stroke(A([pj(x0 + 0.05, 1.3), pj(x0 + W * 0.36, H - 0.1)]), 8, 8, n=2, cap=False))
    m = cv.polys_mask(tf)
    drop_shadow(cv, m, 2, 3, 3, 0.3)
    paint(cv, m, P.WOOD, line=0.8, line_w=1.0, soft=3, ao=0, tex=wood_tex(cv, "tf"))
    stone_band(cv, front[0][0], front[1][0], front[2][1] + (front[0][1] - front[2][1]) * 0.86, front[0][1], r, key + "sb")
    sb = pj.poly([(x1, 0, 0), (x1, 0, D), (x1, 0.35, D), (x1, 0.35, 0)])
    part(cv, sb, P.shadow_of(P.STONE_WARM, 1.0), line=0.6, soft=3, ao=0, tex=cv.noise(8, "ss", 3))
    # door + windows
    dx, dy = pj(x0 + W * 0.18, 0)
    door(cv, dx, dy, 78, 168, col=P.mix(P.WOOD, P.TEAL, 0.25))
    wx, wy = pj(x0 + W * 0.68, 1.45)
    window(cv, wx, wy, 92, 92, kind="round", frame=P.WOOD)
    sx, sy = pj(x0 + W * 0.36 + 0.45, 1.5)
    # side window
    swp = pj.poly([(x1, 1.0, 1.1), (x1, 1.0, 2.2), (x1, 1.8, 2.2), (x1, 1.8, 1.1)])
    m = cv.mask(swp)
    glow_pane(cv, m)
    paint(cv, m, P.WOOD, line=1.0, line_w=3.0, soft=1, opacity=0.0)
    lines(cv, [tuple(swp[0]) + tuple(swp[1]), tuple(swp[1]) + tuple(swp[2]), tuple(swp[2]) + tuple(swp[3]), tuple(swp[3]) + tuple(swp[0])],
          5, P.WOOD_DK, 1.0)
    # roof
    ov = 0.45
    eave_l, eave_r = pj(x0 - ov, H - 0.15, -ov), pj(x1 + ov, H - 0.15, -ov)
    ridge_l, ridge_r = pj(x0 - ov * 0.6, H + 2.2, D / 2), pj(x1 + ov, H + 2.2, D / 2)
    back_r = pj(x1 + ov, H - 0.15, D + ov)
    outline_pts = [eave_l, (eave_r[0], eave_r[1]), back_r, ridge_r, ridge_l]
    side_pts = [eave_r, back_r, ridge_r]
    thatch(cv, outline_pts, key + "th", P.THATCH, down=(-0.25, 1.0), rows=5, side_pts=side_pts, bulge=0.07)
    # dormer-ish eyebrow window in the thatch
    ex, ey = pj(x0 + W * 0.42, H + 1.1, 0.9)
    ebm = cv.mask(ellipse(ex, ey, 70, 50, 0, 30, math.pi, 2 * math.pi))
    paint(cv, ebm, P.light_of(P.THATCH, 0.8), line=0.8, line_w=1.4, soft=10, ao=0)
    window(cv, ex, ey - 12, 46, 26, kind="rect", spill=False, sill=False)
    chimney(cv, pj(x1 - 0.6, 0, 1.2)[0], pj(0, H + 1.7, 1.2)[1], 46, 120, r, key + "ch", crooked=8)
    ivy(cv, [pj(x0 + 0.05, 0.2), pj(x0 + 0.1, 1.2), pj(x0 + 0.4, 2.0), pj(x0 + 0.9, 2.3)], r, key + "iv", size=9, n=70)
    flower_box(cv, wx, wy + 62, 110, FLOWERS)
    grass_base(cv, 120, 900, 962, r, key + "gb", h=30, n=6)
    for i in range(3):
        bx = 120 + i * 18
        ell(cv, bx, 955 - i * 4, 22, 18, P.mix(P.MOSS, P.MOSS_DK, 0.3), line=0.6, soft=6)
    return done(cv, 3.0)


def prop_cottage_b(key="prop_cottage_b"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    pj = Proj(500, 975, 108)
    W, D = 3.4, 3.0
    lean = 0.06

    def L(x, y, z=0):  # crooked: lean grows with height
        return pj(x + lean * y * y * 0.25, y, z)

    def poly(pts):
        return A([L(*p) for p in pts])
    shadow_ellipse(cv, 540, 975, 330, 30)
    x0, x1 = -W / 2, W / 2
    H1, H2 = 2.3, 4.6
    # ground floor stone, upper floor plaster jutting out
    side1 = poly([(x1, 0, 0), (x1, 0, D), (x1, H1, D), (x1, H1, 0)])
    part(cv, side1, P.shadow_of(P.STONE_WARM, 0.9), line=0.8, soft=6, tex=cv.noise(10, "s1", 3))
    front1 = poly([(x0, 0, 0), (x1, 0, 0), (x1, H1, 0), (x0, H1, 0)])
    f1 = cv.mask(front1)
    stone_band(cv, front1[0][0], front1[1][0], front1[2][1], front1[0][1], r, key + "sb", size=34)
    jut = 0.25
    side2 = poly([(x1 + jut, H1, -jut), (x1 + jut, H1, D), (x1 + jut, H2, D), (x1 + jut, H2, -jut)])
    plaster(cv, side2, wall_shade(P.mix(P.PLASTER, P.ROSE, 0.25)), key + "s2", shade=0.2)
    front2 = poly([(x0 - jut, H1, -jut), (x1 + jut, H1, -jut), (x1 + jut, H2, -jut), (x0 - jut, H2, -jut)])
    m2 = plaster(cv, front2, P.mix(P.PLASTER, P.ROSE, 0.25), key + "f2")
    beam = poly([(x0 - jut - 0.1, H1 - 0.05, -jut), (x1 + jut + 0.1, H1 - 0.05, -jut), (x1 + jut + 0.1, H1 + 0.18, -jut),
                 (x0 - jut - 0.1, H1 + 0.18, -jut)])
    part(cv, beam, P.WOOD, line=0.8, soft=3, tex=wood_tex(cv, "bm"))
    gable = poly([(x1 + jut, H2, -jut), (x1 + jut, H2, D), (x1 + jut, H2 + 2.6, D / 2 - jut / 2)])
    plaster(cv, gable, P.scale_v(wall_shade(P.mix(P.PLASTER, P.ROSE, 0.25)), 1.05), key + "g", shade=0.2)
    dx, dy = L(x0 + W * 0.3, 0)
    door(cv, dx, dy, 70, 150, col=P.mix(P.WOOD, P.VERMILION, 0.3))
    wx, wy = L(x0 + W * 0.75, 1.2)
    window(cv, wx, wy, 52, 64, kind="arch", box=FLOWERS)
    for i, xx in enumerate((x0 + W * 0.25, x0 + W * 0.72)):
        wx, wy = L(xx, H1 + 1.15, -jut)
        window(cv, wx, wy, 56, 70, kind="rect", box=FLOWERS if i == 0 else None)
        # shutters
        for s in (-1, 1):
            sh = A([(wx + s * 36, wy - 40), (wx + s * 58, wy - 40), (wx + s * 58, wy + 40), (wx + s * 36, wy + 40)])
            part(cv, sh, P.mix(P.TEAL, P.DUSTY_BLUE, 0.4), line=0.8, line_w=1.0, soft=3, tex=wood_tex(cv, "sh", True))
    # steep tile roof
    ov = 0.35
    eL = L(x0 - jut - ov, H2 - 0.1, -jut - ov)
    eR = L(x1 + jut + ov, H2 - 0.1, -jut - ov)
    rL = L(x0 - jut - ov * 0.5, H2 + 2.6, D / 2 - jut / 2)
    rR = L(x1 + jut + ov, H2 + 2.6, D / 2 - jut / 2)
    bR = L(x1 + jut + ov, H2 - 0.1, D + ov)
    tile_roof(cv, [eL, eR, bR, rR, rL], key + "rf", col=P.TERRACOTTA, rows=8, tile_w=20, side_pts=[eR, bR, rR])
    # tall crooked chimney
    cx, cy = L(x1 - 0.5, H2 + 2.0, 1.2)
    chimney(cv, cx, cy, 44, 210, r, key + "ch", crooked=-22)
    # round attic window in roof
    ax, ay = L(0.1, H2 + 1.3, 0.6)
    ebm = cv.mask(A([(ax - 40, ay + 30), (ax - 36, ay - 26), (ax, ay - 52), (ax + 36, ay - 26), (ax + 40, ay + 30)]))
    paint(cv, ebm, P.mix(P.PLASTER, P.ROSE, 0.25), line=0.8, line_w=1.4, soft=8, ao=0.2)
    window(cv, ax, ay - 2, 34, 34, kind="round", spill=False)
    ivy(cv, [L(x1, 0.1), L(x1 + 0.1, 1.0, 0.5), L(x1, 1.8, 1.4)], r, key + "iv", size=9, n=50)
    grass_base(cv, 170, 870, 975, r, key + "gb", h=30, n=6)
    return done(cv, 3.0)


def prop_cottage_c(key="prop_cottage_c"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    pj = Proj(470, 965, 120)
    W, H, D = 5.4, 2.4, 3.2
    x0, x1 = -W / 2, W / 2
    shadow_ellipse(cv, 540, 965, 410, 34)
    side = pj.poly([(x1, 0, 0), (x1, 0, D), (x1, H, D), (x1, H, 0)])
    plaster(cv, side, wall_shade(P.mix(P.PLASTER, P.CREAM_WARM, 0.5)), key + "s", shade=0.2)
    gable = pj.poly([(x1, H, 0), (x1, H, D), (x1, H + 1.7, D / 2)])
    plaster(cv, gable, P.scale_v(wall_shade(P.PLASTER), 1.05), key + "g", shade=0.2)
    front = pj.poly([(x0, 0, 0), (x1, 0, 0), (x1, H, 0), (x0, H, 0)])
    plaster(cv, front, P.mix(P.PLASTER, P.CREAM_WARM, 0.5), key + "f")
    # wooden plank skirt
    sk = pj.poly([(x0, 0, 0), (x1, 0, 0), (x1, 0.55, 0), (x0, 0.55, 0)])
    msk = cv.mask(sk)
    paint(cv, msk, P.mix(P.WOOD_LIGHT, P.DUSTY_BLUE, 0.25), line=0.8, soft=4, tex=wood_tex(cv, "sk", True))
    xs = np.linspace(sk[0][0], sk[1][0], 22)
    lines(cv, [(x, sk[2][1], x, sk[0][1]) for x in xs], 1.4, P.WOOD_DK, 0.5, clip=msk)
    dx, dy = pj(0.2, 0)
    door(cv, dx, dy, 80, 165, col=P.mix(P.ROOF_BLUE, P.WOOD, 0.4), arch=False, lantern=True)
    for xx in (x0 + 0.95, x1 - 0.9):
        wx, wy = pj(xx, 1.4)
        window(cv, wx, wy, 70, 72, kind="rect", box=FLOWERS)
        for s in (-1, 1):
            sh = A([(wx + s * 42, wy - 42), (wx + s * 64, wy - 42), (wx + s * 64, wy + 42), (wx + s * 42, wy + 42)])
            part(cv, sh, P.ROOF_BLUE, line=0.8, line_w=1.0, soft=3, tex=wood_tex(cv, "sh", True))
    ov = 0.4
    eL, eR = pj(x0 - ov, H - 0.1, -ov), pj(x1 + ov, H - 0.1, -ov)
    rL, rR = pj(x0 - ov * 0.6, H + 1.7, D / 2), pj(x1 + ov, H + 1.7, D / 2)
    bR = pj(x1 + ov, H - 0.1, D + ov)
    tile_roof(cv, [eL, eR, bR, rR, rL], key + "rf", col=P.ROOF_BLUE, rows=6, tile_w=22, side_pts=[eR, bR, rR])
    ridge = stroke(A([rL, rR]), 14, 14, n=2)
    paint(cv, cv.mask(ridge), P.shadow_of(P.ROOF_BLUE, 0.6), line=0.8, soft=3, ao=0)
    cx, cy = pj(x0 + 1.0, H + 1.4, 1.4)
    chimney(cv, cx, cy, 40, 110, r, key + "ch", col=P.mix(P.STONE_WARM, P.TERRACOTTA, 0.3))
    # potted plants & barrel by the door
    for i, px in enumerate((dx - 90, dx + 100)):
        pot = A([(px - 20, dy - 36), (px + 20, dy - 36), (px + 15, dy), (px - 15, dy)])
        bush = cv.mask(blob(px, dy - 58, 30, 28, r, 0.2))
        paint(cv, bush, P.MOSS, line=0.6, soft=6, var=0.15, var_cell=6)
        for k in range(5):
            ell(cv, px + r.normal() * 14, dy - 60 + r.normal() * 12, 5, 5, [P.ROSE, P.WHITE_WARM][i], line=0.5, soft=1, ao=0)
        part(cv, pot, P.TERRACOTTA, line=0.8, soft=3)
    grass_base(cv, 110, 920, 965, r, key + "gb", h=28, n=6)
    return done(cv, 3.0)


def prop_inn(key="prop_inn"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    pj = Proj(455, 975, 92)
    W, D = 7.6, 4.2
    x0, x1 = -W / 2, W / 2
    H1, H2 = 2.6, 5.0
    shadow_ellipse(cv, 520, 975, 450, 34)
    side = pj.poly([(x1, 0, 0), (x1, 0, D), (x1, H2, D), (x1, H2, 0)])
    plaster(cv, side, wall_shade(P.PLASTER), key + "s", shade=0.2)
    gable = pj.poly([(x1, H2, 0), (x1, H2, D), (x1, H2 + 2.6, D / 2)])
    plaster(cv, gable, P.scale_v(wall_shade(P.PLASTER), 1.05), key + "g", shade=0.2)
    front = pj.poly([(x0, 0, 0), (x1, 0, 0), (x1, H2, 0), (x0, H2, 0)])
    mf = plaster(cv, front, P.PLASTER, key + "f")
    stone_band(cv, front[0][0], front[1][0], pj(0, 0.6)[1], front[0][1], r, key + "sb", size=30)
    # timber frame grid
    tf = []
    for k in range(7):
        xx = x0 + 0.06 + k * (W - 0.12) / 6
        tf.append(stroke(A([pj(xx, 0.6), pj(xx, H2)]), 10, 10, n=2, cap=False))
    for yy in (H1, H2 - 0.08):
        tf.append(stroke(A([pj(x0, yy), pj(x1, yy)]), 13, 13, n=2, cap=False))
    for k in range(6):
        xa = x0 + 0.06 + k * (W - 0.12) / 6
        xb = xa + (W - 0.12) / 6
        if k in (0, 5):
            tf.append(stroke(A([pj(xa, H1), pj(xb, H2)]), 8, 8, n=2, cap=False))
    m = cv.polys_mask(tf)
    drop_shadow(cv, m, 2, 3, 3, 0.3)
    paint(cv, m, P.WOOD_DK, line=0.7, line_w=1.0, soft=3, ao=0, tex=wood_tex(cv, "tf"))
    # ground floor: big door, windows
    dx, dy = pj(-0.2, 0)
    door(cv, dx, dy, 92, 170, col=P.mix(P.WOOD, P.VERMILION, 0.2))
    for xx in (x0 + 1.1, x0 + 2.3, x1 - 2.1, x1 - 0.95):
        wx, wy = pj(xx, 1.5)
        window(cv, wx, wy, 56, 64, kind="arch")
    # upper floor windows + balcony
    for xx in (x0 + 0.65, x0 + 1.9, x1 - 1.9, x1 - 0.65):
        wx, wy = pj(xx, H1 + 1.25)
        window(cv, wx, wy, 52, 60, kind="rect", box=FLOWERS if xx < 0 else None)
    bal = pj.poly([(-1.4, H1 + 0.15, -0.5), (1.0, H1 + 0.15, -0.5), (1.0, H1 + 0.35, -0.5), (-1.4, H1 + 0.35, -0.5)])
    part(cv, bal, P.WOOD, line=0.8, soft=3, tex=wood_tex(cv, "bal"))
    rail = []
    for k in range(13):
        xx = -1.4 + k * 0.2
        rail.append(stroke(A([pj(xx, H1 + 0.35, -0.5), pj(xx, H1 + 1.0, -0.5)]), 5, 5, n=2, cap=False))
    rail.append(stroke(A([pj(-1.45, H1 + 1.0, -0.5), pj(1.05, H1 + 1.0, -0.5)]), 9, 9, n=2))
    m = cv.polys_mask(rail)
    drop_shadow(cv, m, 3, 5, 4, 0.3)
    paint(cv, m, P.WOOD_LIGHT, line=0.8, line_w=1.0, soft=2, ao=0)
    bx, by = pj(-0.2, H1 + 1.25)
    window(cv, bx, by + 10, 70, 90, kind="arch", spill=True)
    # roof (terracotta tiles) with dormer
    ov = 0.45
    eL, eR = pj(x0 - ov, H2 - 0.1, -ov), pj(x1 + ov, H2 - 0.1, -ov)
    rL, rR = pj(x0 - ov * 0.6, H2 + 2.6, D / 2), pj(x1 + ov, H2 + 2.6, D / 2)
    bR = pj(x1 + ov, H2 - 0.1, D + ov)
    tile_roof(cv, [eL, eR, bR, rR, rL], key + "rf", col=P.TERRACOTTA, rows=8, tile_w=20, side_pts=[eR, bR, rR])
    for xx in (x0 + 1.6, x1 - 1.8):
        dxx, dyy = pj(xx, H2 + 1.0, 0.8)
        dm = A([(dxx - 46, dyy + 40), (dxx - 46, dyy - 20), (dxx, dyy - 62), (dxx + 46, dyy - 20), (dxx + 46, dyy + 40)])
        plaster(cv, dm, P.PLASTER, key + "dm", shade=0.3)
        window(cv, dxx, dyy + 4, 36, 40, kind="rect", spill=False, sill=False)
        drt = A([(dxx - 60, dyy - 14), (dxx, dyy - 74), (dxx + 60, dyy - 14), (dxx + 50, dyy - 6), (dxx, dyy - 58), (dxx - 50, dyy - 6)])
        part(cv, drt, P.TERRACOTTA_DK, line=0.8, soft=3)
    cx, cy = pj(x1 - 1.0, H2 + 2.0, 1.4)
    chimney(cv, cx, cy, 50, 150, r, key + "ch")
    cx, cy = pj(x0 + 0.8, H2 + 1.6, 1.4)
    chimney(cv, cx, cy, 40, 110, r, key + "ch2", col=P.mix(P.STONE_WARM, P.TERRACOTTA, 0.4))
    # hanging sign "The Sleepy Lantern": a lantern + crescent moon emblem
    sx, sy = pj(x0 - 0.1, H1 - 0.3)
    sline(cv, [(sx, sy), (sx - 110, sy)], 8, P.WOOD_DK, line=0.5)
    for k in (-88, -22):
        sline(cv, [(sx + k, sy), (sx + k, sy + 20)], 2.5, P.INK_SOFT, line=0)
    board = A([(sx - 104, sy + 18), (sx - 6, sy + 18), (sx - 6, sy + 94), (sx - 104, sy + 94)])
    part(cv, catmull(board, True, 3), P.mix(P.WOOD, P.TERRACOTTA, 0.3), line=0.9, line_w=1.4, soft=4, tex=wood_tex(cv, "sg"))
    mx, my = sx - 55, sy + 56
    moon = cv.mask(ellipse(mx - 6, my, 26, 26)) * (1 - cv.mask(ellipse(mx + 6, my - 6, 22, 22)))
    paint(cv, moon.astype(F32), P.CREAM_WARM, line=0.5, soft=2, ao=0, hi=0.5)
    lan = A([(mx + 10, my - 12), (mx + 26, my - 12), (mx + 30, my + 14), (mx + 6, my + 14)])
    glow_pane(cv, cv.mask(lan))
    # hanging lanterns either side of the door
    hanging_lantern(cv, dx - 90, dy - 190, 18, paper=True)
    hanging_lantern(cv, dx + 115, dy - 190, 18, paper=True)
    # benches and barrels
    for bxx in (dx + 190,):
        barrel_at(cv, bxx, dy + 2, 40, r)
        barrel_at(cv, bxx + 60, dy + 6, 36, r)
    ivy(cv, [pj(x1, 0.2), pj(x1 - 0.2, 1.4), pj(x1 - 0.1, 2.4)], r, key + "iv", 9, 60)
    grass_base(cv, 70, 930, 975, r, key + "gb", h=28, n=7)
    return done(cv, 2.8)


def barrel_at(cv, x, by, s, r):
    w = s * 0.42
    body = catmull(A([(x - w * 0.82, by), (x - w * 1.0, by - s * 0.32), (x - w * 1.05, by - s * 0.6), (x - w * 0.84, by - s * 1.0),
                      (x + w * 0.84, by - s * 1.0), (x + w * 1.05, by - s * 0.6), (x + w * 1.0, by - s * 0.32), (x + w * 0.82, by)]), True, 6)
    m = cv.mask(body)
    drop_shadow(cv, m, 3, 3, 4, 0.3)
    paint(cv, m, P.mix(P.WOOD_LIGHT, P.WOOD, 0.3), line=0.8, line_w=1.2, soft=s * 0.3, tex=wood_tex(cv, "br", True), hi=0.45)
    staves = []
    for k in (-0.62, -0.25, 0.12, 0.5):
        staves.append((x + w * k, by - s * 1.0, x + w * k * 1.08, by - s * 0.5))
        staves.append((x + w * k * 1.08, by - s * 0.5, x + w * k, by))
    lines(cv, staves, max(1.2, s * 0.012), P.WOOD_DK, 0.45, clip=m)
    for k in (0.16, 0.42, 0.78):
        yy = by - s * k
        band = cv.mask(stroke(A([(x - w * 1.1, yy - s * 0.02), (x, yy + s * 0.03), (x + w * 1.1, yy - s * 0.02)]), max(2, s * 0.06),
                              max(2, s * 0.06), n=4)) * m
        paint(cv, band.astype(F32), P.mix(P.INK_SOFT, P.DUSTY_BLUE_DK, 0.3), line=0, soft=2, ao=0, hi=0.6, gloss=0.3)
    top = cv.ellipse_mask(x, by - s * 1.0, w * 0.84, s * 0.07)
    paint(cv, top, P.light_of(P.WOOD_LIGHT, 0.8), line=0.8, line_w=1.0, soft=2, ao=0)


# ----------------------------------------------------------------------------------------
# village structures
# ----------------------------------------------------------------------------------------
def prop_shop_stall(key="prop_shop_stall"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 488, 210, 18)
    # back posts
    for x in (82, 430):
        sline(cv, [(x, 488), (x, 150)], 12, P.WOOD, tex=wood_tex(cv, "p", True))
    # goods on back shelf
    shelf = A([(80, 270), (432, 270), (432, 282), (80, 282)])
    part(cv, shelf, P.WOOD_LIGHT, line=0.8, soft=2)
    for i in range(7):
        x = 105 + i * 47
        h = 30 + r.random() * 20
        c = [P.hx("8fb8de"), P.HONEY, P.ROSE, P.SAGE, P.TERRACOTTA][i % 5]
        jar = catmull(A([(x - 14, 270), (x - 16, 270 - h * 0.6), (x - 10, 270 - h), (x + 10, 270 - h), (x + 16, 270 - h * 0.6), (x + 14, 270)]), True, 4)
        part(cv, jar, c, line=0.8, line_w=1.0, soft=4, hi=0.6, gloss=0.4, ao=0.1)
        part(cv, [(x - 9, 268 - h - 6), (x + 9, 268 - h - 6), (x + 9, 271 - h), (x - 9, 271 - h)], P.WOOD, line=0.6, soft=1, ao=0)
    # counter
    ctr = A([(60, 360), (452, 360), (446, 488), (66, 488)])
    mc = cv.mask(ctr)
    paint(cv, mc, P.WOOD, line=0.9, line_w=1.4, soft=10, tex=wood_tex(cv, "c"))
    lines(cv, [(66, y, 446, y) for y in (392, 424, 456)], 2, P.WOOD_DK, 0.6, clip=mc)
    top = A([(48, 344), (464, 344), (460, 364), (52, 364)])
    part(cv, top, P.WOOD_LIGHT, line=0.9, line_w=1.2, soft=3, tex=wood_tex(cv, "t"))
    # produce baskets
    for i, (x, c) in enumerate(((120, P.hx("d9483b")), (215, P.hx("f2b33d")), (310, P.hx("8cbf4f")), (400, P.hx("b5603a")))):
        bk = A([(x - 40, 344), (x + 40, 344), (x + 34, 318), (x - 34, 318)])
        fruit = []
        for k in range(9):
            fruit.append(ellipse(x - 28 + (k % 5) * 14 + (k // 5) * 7, 318 - (k // 5) * 12 - r.random() * 4, 9, 8, 0, 16))
        mfr = cv.polys_mask(fruit)
        paint(cv, mfr, c, line=0.7, line_w=0.8, soft=3, hi=0.6, gloss=0.5, ao=0)
        part(cv, bk, P.THATCH_DK, line=0.8, soft=3, tex=cv.noise((2, 6), "bk", 2))
    # front posts
    for x in (62, 450):
        sline(cv, [(x, 488), (x, 130)], 14, P.WOOD, tex=wood_tex(cv, "fp", True))
    # striped awning
    aw = A([(30, 112), (482, 112), (470, 190), (42, 190)])
    ma = cv.mask(aw)
    drop_shadow(cv, ma, 0, 18, 10, 0.4)
    stripes = ((np.floor((cv.xx() - 30) / 38) % 2) == 0).astype(F32)
    stripes = np.broadcast_to(stripes, ma.shape)
    paint(cv, ma, P.CREAM, line=0.9, line_w=1.4, soft=10, ao=0.3)
    m2 = ma * stripes
    paint(cv, m2.astype(F32), P.TERRACOTTA, line=0.0, soft=10, ao=0.3)
    sc = []
    for k in range(12):
        x = 42 + k * 38
        sc.append(ellipse(x + 19, 190, 19, 16, 0, 16, 0, math.pi))
    msc = cv.polys_mask(sc)
    stripes2 = ((np.floor((cv.xx() - 42) / 38) % 2) == 0).astype(F32)
    paint(cv, msc, P.CREAM, line=0.8, line_w=1.2, soft=4, ao=0)
    paint(cv, (msc * np.broadcast_to(stripes2, msc.shape)).astype(F32), P.TERRACOTTA, line=0.0, soft=4, ao=0)
    hanging_lantern(cv, 470, 200, 14, paper=True)
    return done(cv, 2.4)


def prop_smithy(key="prop_smithy"):
    cv = new(key, 1024, 512)
    r = rng(key)
    shadow_ellipse(cv, 520, 486, 450, 22)
    # stone forge (back right) with chimney
    forge = A([(600, 486), (600, 300), (860, 300), (860, 486)])
    mf = cv.mask(forge)
    paint(cv, mf, P.STONE_WARM, line=0.8, soft=10, ao=0.2)
    stone_band(cv, 600, 860, 300, 486, r, key + "fs", col=P.mix(P.STONE_WARM, P.BRICK, 0.25), size=28)
    mouth = catmull(A([(660, 470), (660, 400), (730, 370), (800, 400), (800, 470)]), True, 6)
    mm = cv.mask(mouth)
    paint(cv, mm, P.INK_SOFT, line=0.6, soft=6, ao=0)
    coals = cv.mask(catmull(A([(668, 470), (680, 430), (730, 415), (790, 430), (792, 470)]), True, 6))
    glow_pane(cv, coals)
    cv.atop(P.EMBER, coals * smoothstep(420, 470, cv.yy()) * 0.7)
    glow(cv, 730, 440, 160, P.AMBER, 0.45, clip=True)
    ch = A([(680, 300), (700, 40), (790, 40), (810, 300)])
    stone_band(cv, 690, 800, 40, 300, r, key + "cs", col=P.STONE_WARM, size=26)
    m_ch = cv.mask(ch)
    cv.erase(np.clip(cv.mask(A([(600, 0), (1024, 0), (1024, 300), (600, 300)])) - m_ch - mf, 0, 1) * 0)
    # back wall + shed roof on posts
    bw = A([(90, 486), (90, 210), (590, 190), (590, 486)])
    mbw = cv.mask(bw)
    paint(cv, mbw, P.mix(P.WOOD, P.WOOD_DK, 0.4), line=0.8, soft=20, ao=0.3, tex=wood_tex(cv, "bw", True))
    lines(cv, [(x, 190, x, 486) for x in np.linspace(110, 570, 12)], 2, P.WOOD_DK, 0.5, clip=mbw)
    for x in (100, 580):
        sline(cv, [(x, 486), (x, 190)], 18, P.WOOD, tex=wood_tex(cv, "p", True))
    roof = [(40, 230), (640, 196), (660, 150), (70, 110)]
    tile_roof(cv, roof, key + "rf", col=P.mix(P.WOOD, P.TERRACOTTA, 0.45), rows=5, tile_w=22)
    eave = stroke(A([(36, 232), (644, 198)]), 12, 12, n=2)
    paint(cv, cv.mask(eave), P.WOOD_DK, line=0.8, soft=3, ao=0)
    # anvil on a stump
    stump = A([(250, 486), (256, 410), (330, 410), (336, 486)])
    part(cv, stump, P.WOOD, line=0.8, soft=6, tex=wood_tex(cv, "st", True))
    anv = A([(220, 410), (360, 410), (350, 392), (390, 382), (390, 370), (300, 370), (290, 360), (232, 360), (236, 380), (230, 392)])
    part(cv, anv, P.mix(P.STONE_DK, P.INK, 0.3), line=0.9, line_w=1.2, soft=6, hi=0.6, gloss=0.4)
    sline(cv, [(300, 358), (338, 320)], 7, P.WOOD, line=0.6)
    part(cv, [(326, 312), (356, 326), (348, 340), (318, 326)], P.STONE_DK, line=0.7, soft=2, hi=0.5)
    # tools on rack + water barrel + glowing ingot
    rack = A([(130, 240), (240, 240), (240, 252), (130, 252)])
    part(cv, rack, P.WOOD_LIGHT, line=0.8, soft=2)
    for i, x in enumerate((145, 175, 205, 230)):
        sline(cv, [(x, 252), (x + 3, 330)], 5, P.WOOD, line=0.5)
        part(cv, [(x - 10, 320), (x + 14, 320), (x + 14, 340), (x - 10, 340)], P.STONE_DK, line=0.6, soft=2, hi=0.5)
    barrel_at(cv, 480, 486, 70, r)
    ell(cv, 480, 486 - 91, 33, 9, P.mix(P.DUSTY_BLUE, P.TEAL, 0.5), line=0.4, soft=2, ao=0, hi=0.6)
    ing = A([(250, 360), (290, 360), (286, 352), (254, 352)])
    glow_pane(cv, cv.mask(ing))
    sacks(cv, 930, 486, r)
    grass_base(cv, 40, 1000, 486, r, key + "gb", h=24, n=6)
    return done(cv, 2.6)


def sacks(cv, x, by, r):
    for i, (dx, s) in enumerate(((-30, 50), (20, 46), (-6, 40))):
        yy = by - (i // 2) * 40
        sk = catmull(A([(x + dx - s * 0.5, yy), (x + dx - s * 0.55, yy - s * 0.6), (x + dx - s * 0.2, yy - s * 1.05), (x + dx + s * 0.25, yy - s * 1.0),
                        (x + dx + s * 0.55, yy - s * 0.6), (x + dx + s * 0.5, yy)]), True, 5)
        part(cv, sk, P.mix(P.CREAM_WARM, P.WOOD_LIGHT, 0.4), line=0.8, soft=s * 0.25, tex=cv.noise(3, "sk", 2), hi=0.4)


def prop_windmill(key="prop_windmill"):
    cv = new(key, 512, 1024)
    r = rng(key)
    shadow_ellipse(cv, 262, 1000, 170, 18)
    base = A([(140, 1000), (170, 560), (342, 560), (372, 1000)])
    mb = cv.mask(base)
    paint(cv, mb, P.STONE_WARM, line=0.8, soft=30, ao=0.2)
    stone_band(cv, 140, 372, 700, 1000, r, key + "sb", size=34)
    cv.erase(np.clip(cv.mask(A([(100, 690), (420, 690), (420, 1010), (100, 1010)])) - mb, 0, 1))
    upper = A([(170, 700), (190, 400), (322, 400), (342, 700)])
    mu = cv.mask(upper)
    paint(cv, mu, P.PLASTER, line=0.8, soft=30, ao=0.2, tex=cv.noise(20, "pl", 3) * 0.4 + 0.3)
    xs = np.linspace(175, 340, 9)
    lines(cv, [(x, 400, x + (256 - x) * -0.06, 700) for x in xs], 1.5, P.shadow_of(P.PLASTER, 1.0), 0.35, clip=mu)
    door(cv, 256, 1000, 70, 130, col=P.mix(P.WOOD, P.TEAL, 0.3))
    window(cv, 256, 620, 40, 52, kind="arch")
    window(cv, 236, 470, 30, 36, kind="rect", spill=False)
    gal = A([(150, 700), (362, 700), (362, 716), (150, 716)])
    part(cv, gal, P.WOOD, line=0.8, soft=2)
    rail = [stroke(A([(x, 700), (x, 666)]), 4, 4, n=2, cap=False) for x in np.linspace(156, 356, 12)]
    rail.append(stroke(A([(150, 666), (362, 666)]), 6, 6, n=2))
    part_m = cv.polys_mask(rail)
    paint(cv, part_m, P.WOOD_LIGHT, line=0.7, soft=2, ao=0)
    cap = catmull(A([(170, 410), (180, 340), (230, 300), (256, 290), (282, 300), (332, 340), (342, 410)]), True, 6)
    thatch(cv, cap, key + "cap", P.mix(P.THATCH, P.TERRACOTTA, 0.15), down=(0, 1), rows=3, bulge=0.04)
    hub = (256, 378)
    blades = []
    sails = []
    for k in range(4):
        a = k * math.pi / 2 + 0.42
        ca, sa = math.cos(a), math.sin(a)
        nx, ny = -sa, ca
        L = 250
        blades.append(stroke(A([hub, (hub[0] + ca * L, hub[1] + sa * L)]), 10, 7, n=2))
        q0 = (hub[0] + ca * 50, hub[1] + sa * 50)
        q1 = (hub[0] + ca * L, hub[1] + sa * L)
        sails.append(A([q0, q1, (q1[0] + nx * 50, q1[1] + ny * 50), (q0[0] + nx * 50, q0[1] + ny * 50)]))
    ms = cv.polys_mask(sails)
    drop_shadow(cv, ms, 6, 10, 8, 0.35)
    paint(cv, ms, P.CREAM, line=0.8, line_w=1.2, soft=8, ao=0, hi=0.3, tex=cv.noise(6, "cl", 2) * 0.3 + 0.35)
    lat = []
    for k in range(4):
        a = k * math.pi / 2 + 0.42
        ca, sa = math.cos(a), math.sin(a)
        nx, ny = -sa, ca
        for j in range(1, 9):
            d = 50 + j * 25
            p = (hub[0] + ca * d, hub[1] + sa * d)
            lat.append((p[0], p[1], p[0] + nx * 50, p[1] + ny * 50))
        for f in (0.5, 1.0):
            lat.append((hub[0] + ca * 50 + nx * 50 * f, hub[1] + sa * 50 + ny * 50 * f, hub[0] + ca * 250 + nx * 50 * f,
                        hub[1] + sa * 250 + ny * 50 * f))
    lines(cv, lat, 3, P.WOOD, 0.85, clip=np.clip(ms * 1.5, 0, 1))
    mb2 = cv.polys_mask(blades)
    paint(cv, mb2, P.WOOD, line=0.8, soft=3, ao=0, tex=wood_tex(cv, "bl"))
    ell(cv, hub[0], hub[1], 16, 16, P.WOOD_DK, line=0.8, soft=4, hi=0.5)
    grass_base(cv, 130, 390, 1000, r, key + "gb", h=30, n=4)
    return done(cv, 2.6)


def prop_well(key="prop_well"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 490, 190, 20)
    for x in (130, 382):
        sline(cv, [(x, 440), (x, 120)], 14, P.WOOD, tex=wood_tex(cv, "p", True))
    sline(cv, [(130, 190), (382, 190)], 9, P.WOOD_DK, line=0.6)
    # crank
    sline(cv, [(382, 190), (410, 190), (410, 222)], 6, P.INK_SOFT, line=0.4)
    # rope and bucket
    sline(cv, [(250, 194), (250, 300)], 3, P.THATCH_DK, line=0)
    bk = A([(226, 300), (274, 300), (268, 342), (232, 342)])
    part(cv, bk, P.WOOD_LIGHT, line=0.8, soft=4, tex=wood_tex(cv, "bk", True))
    lines(cv, [(228, 312, 272, 312), (230, 332, 270, 332)], 3, P.INK_SOFT, 0.8, clip=cv.mask(bk))
    # stone ring
    ring = catmull(A([(100, 490), (96, 380), (120, 360), (256, 350), (392, 360), (416, 380), (412, 490)]), True, 6)
    mr = cv.mask(ring)
    drop_shadow(cv, mr, 4, 5, 6, 0.3)
    paint(cv, mr, P.STONE_WARM, line=0.8, soft=20, ao=0.2)
    stone_band(cv, 98, 414, 372, 490, r, key + "sb", size=30)
    cv.erase(np.clip(cv.mask(A([(60, 365), (452, 365), (452, 500), (60, 500)])) - mr, 0, 1))
    rim = cv.ellipse_mask(256, 368, 160, 26)
    paint(cv, rim, P.light_of(P.STONE_WARM, 0.6), line=0.8, soft=6, ao=0)
    hole = cv.ellipse_mask(256, 368, 130, 16)
    paint(cv, hole, P.mix(P.INK, P.TEAL, 0.3), line=0.4, soft=4, ao=0, hi=0)
    moss_on(cv, key, cell=36, thr=0.6, where=mr)
    # little roof
    rf = A([(90, 140), (256, 50), (422, 140), (408, 156), (256, 76), (104, 156)])
    rfm = cv.mask(catmull(A([(80, 150), (180, 88), (256, 46), (332, 88), (432, 150), (420, 166), (256, 92), (92, 166)]), True, 4))
    thatch_mask = rfm
    drop_shadow(cv, rfm, 4, 10, 8, 0.4)
    tile_roof(cv, [(80, 150), (256, 44), (432, 150), (420, 168), (256, 70), (92, 168)], key + "rf", col=P.TERRACOTTA, rows=5,
              tile_w=16)
    ivy(cv, [(130, 440), (128, 330), (140, 230)], r, key + "iv", 7, 40)
    grass_base(cv, 90, 420, 490, r, key + "gb", h=26, n=4)
    return done(cv, 2.4)


def prop_fence(key="prop_fence"):
    cv = new(key, 512, 256)
    r = rng(key)
    shadow_ellipse(cv, 256, 236, 240, 10, 0.25)
    posts = [(22, 236), (256, 238), (490, 236)]
    for i, (x, by) in enumerate(posts):
        top = by - 150 - r.random() * 10
        pts = A([(x - 11, by), (x - 10, top + 8), (x - 4, top), (x + 4, top - 2), (x + 11, top + 6), (x + 11, by)])
        part(cv, pts, P.WOOD, line=0.9, line_w=1.2, soft=4, tex=wood_tex(cv, "p%d" % i, True))
    for y0 in (130, 180):
        for k in range(2):
            xa, xb = posts[k][0], posts[k + 1][0]
            sag = r.normal() * 4
            rail = stroke(A([(xa - 4, y0 + r.normal() * 3), ((xa + xb) / 2, y0 + 3 + sag), (xb + 4, y0 + r.normal() * 3)]), 14, 13, n=6)
            m = cv.mask(rail)
            drop_shadow(cv, m, 2, 4, 3, 0.3)
            paint(cv, m, P.WOOD_LIGHT, line=0.9, line_w=1.2, soft=3, tex=wood_tex(cv, "r%d%d" % (y0, k)))
    grass_base(cv, 10, 500, 238, r, key + "gb", h=34, n=7)
    return done(cv, 2.2)


def prop_lamp_post(key="prop_lamp_post"):
    cv = new(key, 256, 512)
    r = rng(key)
    shadow_ellipse(cv, 120, 496, 60, 9, 0.3)
    post = A([(98, 496), (102, 80), (118, 70), (122, 496)])
    part(cv, post, P.WOOD, line=0.9, line_w=1.2, soft=4, tex=wood_tex(cv, "p", True))
    sline(cv, [(110, 92), (196, 92)], 10, P.WOOD_DK, line=0.6)
    sline(cv, [(120, 130), (160, 94)], 6, P.WOOD_DK, line=0.6)
    hanging_lantern(cv, 186, 98, 30, paper=True)
    base = A([(86, 496), (92, 470), (128, 470), (134, 496)])
    part(cv, base, P.STONE_WARM, line=0.8, soft=3)
    grass_base(cv, 70, 160, 496, r, key + "gb", h=24, n=3)
    return done(cv, 2.0)


def toro(cv, cx, by, s, key, dark=False):
    """Stone shrine lantern (tōrō). s = pixels per unit (~ total height 8.5 units)."""
    stone = P.mix(P.STONE, P.STONE_WARM, 0.5) if not dark else P.mix(P.BLIGHT, P.STONE_DK, 0.3)
    U = s

    def box(x0, y0, x1, y1, col, **kw):
        return part(cv, [(cx + x0 * U, by - y0 * U), (cx + x1 * U, by - y0 * U), (cx + x1 * U, by - y1 * U), (cx + x0 * U, by - y1 * U)],
                    col, **kw)
    kw = dict(line=0.85, line_w=1.3, soft=6, hi=0.4, ao=0.2, var=0.1, var_cell=12)
    shadow_ellipse(cv, cx + 10, by, 3.0 * U, 0.35 * U, 0.35)
    box(-1.6, 0, 1.6, 0.6, stone, **kw)                       # base
    part(cv, catmull(A([(cx - 1.3 * U, by - 0.6 * U), (cx - 1.0 * U, by - 0.95 * U), (cx + 1.0 * U, by - 0.95 * U), (cx + 1.3 * U, by - 0.6 * U)]), True, 4),
         stone, **kw)
    box(-0.45, 0.9, 0.45, 3.6, stone, **kw)                   # pillar
    box(-1.3, 3.6, 1.3, 4.1, stone, **kw)                     # platform
    lb = box(-0.95, 4.1, 0.95, 5.6, stone, **kw)              # light box
    win = A([(cx - 0.55 * U, by - 4.35 * U), (cx + 0.55 * U, by - 4.35 * U), (cx + 0.55 * U, by - 5.35 * U), (cx - 0.55 * U, by - 5.35 * U)])
    mw = cv.mask(win)
    if dark:
        paint(cv, mw, P.mix(P.INK, P.BLIGHT_VIOLET, 0.25), line=0.5, soft=4, ao=0, hi=0)
        glow(cv, cx, by - 4.85 * U, 0.6 * U, P.BLIGHT_GLOW, 0.35, clip=True)
    else:
        glow_pane(cv, mw)
        glow(cv, cx, by - 4.85 * U, 2.2 * U, P.LANTERN, 0.4, clip=True)
    lines(cv, [(cx, by - 4.35 * U, cx, by - 5.35 * U)], 0.12 * U, stone, 0.9, clip=mw)
    # roof with curled corners
    roof = catmull(A([(cx - 2.0 * U, by - 5.55 * U), (cx - 2.2 * U, by - 5.95 * U), (cx - 1.2 * U, by - 6.1 * U), (cx - 0.4 * U, by - 6.9 * U),
                      (cx + 0.4 * U, by - 6.9 * U), (cx + 1.2 * U, by - 6.1 * U), (cx + 2.2 * U, by - 5.95 * U), (cx + 2.0 * U, by - 5.55 * U),
                      (cx, by - 5.75 * U)]), True, 5)
    mr = cv.mask(roof)
    drop_shadow(cv, mr, 3, 6, 5, 0.4)
    paint(cv, mr, stone, **kw)
    ell(cv, cx, by - 7.15 * U, 0.38 * U, 0.38 * U, stone, **kw)
    part(cv, catmull(A([(cx, by - 7.9 * U), (cx + 0.25 * U, by - 7.45 * U), (cx, by - 7.3 * U), (cx - 0.25 * U, by - 7.45 * U)]), True, 4),
         stone, **kw)
    # moss
    mo = np.zeros_like(cv.a)
    if dark:
        bl = cv.a * smoothstep(0.58, 0.66, cv.noise(26, key + "bl", 3)) * (cv.a > 0.95)
        wash(cv, bl.astype(F32), P.BLIGHT_DK, 0.6, pool=0.4, gran=0.2, key="bm")
        # cracks
        r = rng(key, "ck")
        for i in range(5):
            x = cx + (r.random() - 0.5) * 1.6 * U
            y = by - (1 + r.random() * 5) * U
            pts = [(x, y)]
            for k in range(4):
                x += r.normal() * 0.2 * U
                y += 0.25 * U
                pts.append((x, y))
            m = cv.mask(stroke(A(pts), 0.08 * U, 0.02 * U, n=3)) * (cv.a > 0.5)
            cv.atop(P.INK, m * 0.7)
        # blight crystals at the base
        crystal_cluster(cv, cx - 1.4 * U, by, 0.9 * U, r, key + "cr", n=4)
        crystal_cluster(cv, cx + 1.5 * U, by + 2, 0.7 * U, r, key + "cr2", n=3)
    else:
        moss_on(cv, key, cell=26, thr=0.62, top_bias=smoothstep(by - 5.6 * U, by - 6.6 * U, cv.yy()) * 0.3)


def crystal_cluster(cv, x, by, s, r, key, n=5, col=P.BLIGHT_VIOLET, glow_amt=0.5):
    """Faceted crystal shards: lit left facet, shadowed right facet, bright tips, inner glow."""
    shards = []
    for i in range(n):
        off = (i - (n - 1) / 2)
        a = -math.pi / 2 + off * 0.32 + r.normal() * 0.1
        L = s * (0.55 + r.random() * 0.5) * (1.35 if i == n // 2 else 1.0)
        w = s * (0.13 + r.random() * 0.06) * (1.25 if i == n // 2 else 1.0)
        bx = x + off * s * 0.2
        shards.append((abs(off), bx, a, L, w))
    lit = P.mix(col, P.BLIGHT_GLOW, 0.55)
    dk = P.mix(col, P.BLIGHT_DK, 0.6)
    for (o, bx, a, L, w) in sorted(shards, key=lambda t: -t[0]):
        ca, sa = math.cos(a), math.sin(a)
        nx, ny = -sa, ca
        base_l = (bx - nx * w, by - ny * w * 0.3 + 2)
        base_r = (bx + nx * w, by + ny * w * 0.3 + 2)
        sh_l = (bx - nx * w + ca * L * 0.78, by - ny * w + sa * L * 0.78)
        sh_r = (bx + nx * w + ca * L * 0.78, by + ny * w + sa * L * 0.78)
        tip = (bx + ca * L, by + sa * L)
        ridge_b = (bx + nx * w * 0.15, by + 2)
        ridge_t = (bx + ca * L * 0.8 + nx * w * 0.15, by + sa * L * 0.8)
        whole = cv.mask(A([base_l, sh_l, tip, sh_r, base_r]))
        drop_shadow(cv, whole, 2, 3, 3, 0.3)
        left = cv.mask(A([base_l, sh_l, tip, ridge_t, ridge_b])) * whole
        right = cv.mask(A([ridge_b, ridge_t, tip, sh_r, base_r])) * whole
        paint(cv, left.astype(F32), lit, line=0.0, soft=2, ao=0.3, hi=0.2, var=0.04, flat=True)
        paint(cv, right.astype(F32), dk, line=0.0, soft=2, ao=0.2, var=0.04, flat=True)
        # bottom-to-top gradient: dark base, glowing upper part
        g = whole * smoothstep(by, by + sa * L, cv.yy())
        cv.atop(P.BLIGHT_GLOW, g * 0.35)
        paint(cv, whole, col, line=0.9, line_w=1.2, soft=2, opacity=0.0)
        edge = np.clip(whole - cv.blur(whole, 1.0), 0, 1)
        cv.atop(P.mix(P.BLIGHT_DK, P.INK, 0.3), edge * 1.2)
        lines(cv, [(ridge_b[0], ridge_b[1], ridge_t[0], ridge_t[1]), (ridge_t[0], ridge_t[1], tip[0], tip[1])], max(1, s * 0.012),
              P.WHITE_WARM, 0.55, clip=whole)
    glow(cv, x, by - s * 0.45, s * 1.1, P.BLIGHT_GLOW, glow_amt * 0.35, clip=True)


def prop_spirit_lantern(key="prop_spirit_lantern"):
    cv = new(key, 256, 512)
    toro(cv, 128, 492, 56, key)
    r = rng(key)
    grass_base(cv, 30, 226, 494, r, key + "gb", h=22, n=4)
    return done(cv, 2.0)


def prop_spirit_lantern_dark(key="prop_spirit_lantern_dark"):
    cv = new(key, 256, 512)
    toro(cv, 128, 492, 56, key, dark=True)
    return done(cv, 2.0)


# ----------------------------------------------------------------------------------------
# trees and vegetation
# ----------------------------------------------------------------------------------------
def branchy_trunk(cv, x, by, h, w, r, key, col=P.WOOD, n_br=4, top=0.55, bark=True, roots=True, spread=0.5):
    """Trunk with root flare and a few branches. Returns list of branch tips (design coords)."""
    trunk_top = by - h * top
    pts = [(x, by), (x + r.normal() * w * 0.1, by - h * top * 0.35), (x + r.normal() * w * 0.2, by - h * top * 0.7), (x, trunk_top)]
    polys = [stroke(A(pts), w, w * 0.55, n=8)]
    if roots:
        polys.append(catmull(A([(x - w * 1.2, by + 2), (x - w * 0.5, by - w * 0.5), (x, by - w * 1.2), (x + w * 0.5, by - w * 0.5),
                                (x + w * 1.3, by + 2)]), True, 6))
    tips = []
    for i in range(n_br):
        side = -1 if i % 2 == 0 else 1
        t0 = 0.45 + 0.5 * i / max(1, n_br)
        sx, sy = x, by - h * top * t0
        a = -math.pi / 2 + side * (0.5 + r.random() * spread)
        L = h * (0.22 + r.random() * 0.14)
        ex, ey = sx + math.cos(a) * L, sy + math.sin(a) * L
        mx, my = sx + math.cos(a) * L * 0.5 + side * L * 0.1, sy + math.sin(a) * L * 0.5
        polys.append(stroke(A([(sx, sy), (mx, my), (ex, ey)]), w * 0.42, w * 0.12, n=6))
        tips.append((ex, ey))
    tips.append((x, trunk_top - h * 0.05))
    m = cv.polys_mask(polys)
    tex = cv.noise((40, 3), key + "bark", 3) if bark else None
    paint(cv, m, col, line=0.9, line_w=1.4, soft=w * 0.3, key=key, tex=tex, hi=0.3, ao=0.1)
    return tips, m


def canopy(cv, clusters, col, r, key, light=P.GRASS_LIGHT, leafy=True, line=0.6, dark=None, clump=0.36):
    """Ghibli-style foliage: each volume (cx, cy, rx, ry) is built from many shaded leaf clumps
    (back/top first), lit from the upper left, with small leaf dabs catching the light."""
    dark = dark or P.mix(col, P.FOREST_DK, 0.5)
    clumps = []
    for (cx, cy, rx, ry) in clusters:
        n = int(6 + (rx * ry) / (rx * clump * ry * clump) * 0.55)
        for k in range(n):
            # sample inside the ellipse, biased to the rim so the silhouette is bumpy
            a = r.random() * 2 * math.pi
            d = math.sqrt(r.random()) * 0.82
            px, py = cx + math.cos(a) * rx * d, cy + math.sin(a) * ry * d
            cr = rx * clump * (0.75 + 0.5 * r.random())
            clumps.append((px, py, cr, cr * (0.72 + 0.15 * r.random()), cy, ry))
    ys = [c[1] for c in clumps]
    y0, y1 = min(ys), max(ys)
    # back-to-front: top & centre first, lower/outer clumps over them
    clumps.sort(key=lambda c: c[1] + abs(c[0] - np.mean([cc[0] for cc in clumps])) * 0.15)
    allm = np.zeros((cv.PH, cv.PW), F32)
    lc = P.mix(col, light, 0.5)
    for i, (px, py, rx, ry, ccy, cry) in enumerate(clumps):
        sub = cv.sub(px - rx * 1.6, py - ry * 1.8, px + rx * 1.6, py + ry * 1.8)
        if sub is None:
            continue
        sc = []
        nb = 9
        ph = r.random() * 6.28
        for k in range(nb * 2):
            aa = ph + k / (nb * 2) * 2 * math.pi
            bump = 1.0 + (0.1 if k % 2 == 0 else -0.04) + (r.random() - 0.5) * 0.08
            sc.append((px + math.cos(aa) * rx * bump, py + math.sin(aa) * ry * bump))
        m = sub.mask(catmull(A(sc), True, 4))
        drop_shadow(sub, m, rx * 0.08, ry * 0.18, rx * 0.12, 0.28)
        t = (py - y0) / max(1.0, y1 - y0)          # 0 top .. 1 bottom
        c = P.mix(P.mix(col, light, 0.22), dark, 0.1 + 0.5 * t)
        paint(sub, m, c, line=line, line_w=1.2, soft=rx * 0.4, key=key + "c%d" % (i % 7), ao=0.35, hi=0.4, shade=0.95,
              var=0.04, var_cell=rx * 0.5, cel=0.35, light_col=P.mix(light, P.CREAM_WARM, 0.3),
              line_col=P.mix(c, P.INK, 0.45))
        if leafy:
            dabs = []
            nd = int(3 + rx / 7)
            for k in range(nd):
                aa = math.pi * (1.05 + 0.6 * r.random())
                d = 0.25 + 0.6 * r.random()
                qx, qy = px + math.cos(aa) * rx * d, py + math.sin(aa) * ry * d
                L = rx * (0.16 + 0.1 * r.random())
                dabs.append(leaf_poly(qx, qy, L, -0.6 + r.normal() * 0.5, 0.55))
            dm = sub.polys_mask(dabs) * m
            sub.over(np.asarray(P.mix(lc, P.CREAM_WARM, 0.15 * (1 - t)), F32), dm * (0.75 - 0.4 * t))
        allm_s = allm[sub.sl] if False else None
    allm = cv.a.copy()
    return allm


def prop_tree_oak(key="prop_tree_oak"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    shadow_ellipse(cv, 530, 990, 300, 30, 0.35)
    tips, _ = branchy_trunk(cv, 512, 990, 900, 70, r, key + "tr", n_br=5, top=0.5)
    cl = [(512, 360, 300, 210)]
    for (tx, ty) in tips:
        cl.append((tx + r.normal() * 20, ty - 30, 150 + r.random() * 70, 110 + r.random() * 40))
    cl += [(340, 300, 190, 150), (690, 310, 200, 150), (512, 200, 220, 150), (400, 470, 170, 110), (640, 470, 170, 110)]
    canopy(cv, cl, P.mix(P.MOSS, P.SAGE, 0.25), r, key + "cn")
    grass_base(cv, 380, 660, 992, r, key + "gb", h=40, n=4)
    return done(cv, 3.0)


def prop_tree_pine(key="prop_tree_pine"):
    cv = new(key, 512, 1024)
    r = rng(key)
    shadow_ellipse(cv, 266, 996, 170, 20, 0.35)
    trunk = stroke(A([(256, 1000), (258, 700), (256, 120)]), 46, 16, n=6)
    paint(cv, cv.mask(trunk), P.mix(P.WOOD, P.TERRACOTTA, 0.25), line=0.9, soft=8, tex=cv.noise((40, 3), "bk", 3))
    tiers = 9
    for i in range(tiers):
        t = i / (tiers - 1)
        ty = 60 + t * 780
        hw = 40 + t * 190
        th = 120 + t * 40
        pts = [(256, ty)]
        nseg = 6
        for k in range(nseg + 1):
            u = k / nseg
            pts.append((256 + hw * u, ty + th * (0.25 + 0.75 * u ** 1.4) + (k % 2) * 10))
        pts.append((256 + hw * 0.85, ty + th + 14))
        pts.append((256, ty + th * 0.8))
        pts.append((256 - hw * 0.85, ty + th + 14))
        for k in range(nseg, -1, -1):
            u = k / nseg
            pts.append((256 - hw * u, ty + th * (0.25 + 0.75 * u ** 1.4) + (k % 2) * 10))
        m = cv.mask(catmull(A(pts), True, 3))
        m = warp(cv, m, 3, 18, "pw%d" % i)
        drop_shadow(cv, m, 4, 14, 8, 0.35)
        c = P.mix(P.FOREST, P.MOSS_DK, 0.25 + 0.5 * t)
        paint(cv, m, c, line=0.8, line_w=1.4, soft=hw * 0.25, key=key + str(i), ao=0.6, hi=0.4, var=0.08, var_cell=12, cel=0.45,
              light_col=P.mix(P.SAGE, P.CREAM, 0.2))
        # needle strokes
        segs = []
        for k in range(int(hw / 3)):
            x = 256 + (r.random() * 2 - 1) * hw * 0.9
            y = ty + th * (0.3 + 0.6 * r.random())
            segs.append((x, y, x + (x - 256) * 0.06, y + 10))
        lines(cv, segs, 1.6, P.light_of(c, 1.0), 0.5, clip=m)
    grass_base(cv, 150, 370, 1000, r, key + "gb", h=34, n=3)
    return done(cv, 2.8)


def prop_tree_birch(key="prop_tree_birch"):
    cv = new(key, 512, 1024)
    r = rng(key)
    shadow_ellipse(cv, 262, 996, 150, 18, 0.3)
    polys = [stroke(A([(250, 1000), (262, 700), (244, 420), (258, 180)]), 38, 18, n=8)]
    tips = []
    for i, (sy, side, L) in enumerate(((560, -1, 150), (470, 1, 160), (380, -1, 130), (300, 1, 120))):
        ex, ey = 252 + side * L, sy - L * 0.8
        polys.append(stroke(A([(252, sy), (252 + side * L * 0.5, sy - L * 0.5), (ex, ey)]), 14, 5, n=6))
        tips.append((ex, ey))
    m = cv.polys_mask(polys)
    paint(cv, m, P.hx("f1ece2"), line=0.9, line_w=1.4, soft=8, key=key + "t", hi=0.3)
    # birch marks
    marks = []
    for i in range(40):
        y = 200 + r.random() * 790
        x = 254 + (r.random() - 0.5) * 30
        marks.append(ellipse(x, y, 4 + r.random() * 9, 1.6 + r.random() * 2.2, r.normal() * 0.1, 10))
    mk = cv.polys_mask(marks) * m
    cv.atop(P.INK_SOFT, mk * 0.85)
    cl = [(256, 230, 170, 130), (140, 340, 120, 100), (370, 320, 125, 100), (180, 470, 110, 80), (340, 450, 115, 85),
          (256, 120, 110, 90)]
    for (tx, ty) in tips:
        cl.append((tx, ty, 80 + r.random() * 30, 60 + r.random() * 20))
    canopy(cv, cl, P.mix(P.GRASS_LIGHT, P.LEAF_YELLOW, 0.25), r, key + "cn", light=P.CREAM_WARM)
    grass_base(cv, 160, 360, 1000, r, key + "gb", h=32, n=3)
    return done(cv, 2.8)


def prop_tree_great(key="prop_tree_great"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    shadow_ellipse(cv, 530, 1000, 380, 30, 0.4)
    # massive trunk with buttress roots
    polys = [stroke(A([(512, 1004), (500, 800), (520, 620), (505, 470)]), 190, 120, n=8)]
    for side in (-1, 1):
        for k in range(2):
            rx = 512 + side * (120 + k * 70)
            polys.append(stroke(A([(512 + side * 60, 830 - k * 30), (512 + side * (90 + k * 50), 930), (rx + side * 40, 1004)]),
                                70 - k * 15, 40, n=6))
    tips = []
    for i, (sy, side, L, a_off) in enumerate(((560, -1, 330, 0.75), (520, 1, 340, 0.7), (480, -1, 230, 0.35), (470, 1, 240, 0.4),
                                             (500, 0, 200, 0.0))):
        a = -math.pi / 2 + side * a_off
        ex, ey = 510 + math.cos(a) * L, sy + math.sin(a) * L
        polys.append(stroke(A([(510, sy), (510 + math.cos(a) * L * 0.5 + side * 30, sy + math.sin(a) * L * 0.55), (ex, ey)]),
                            70, 26, n=6))
        tips.append((ex, ey))
    m = cv.polys_mask(polys)
    paint(cv, m, P.mix(P.WOOD, P.STONE_DK, 0.25), line=0.9, line_w=1.6, soft=40, key=key + "t",
          tex=cv.noise((60, 4), "bark", 3), hi=0.35, ao=0.2)
    # bark ridges
    segs = []
    for i in range(60):
        x = 420 + r.random() * 190
        y = 560 + r.random() * 420
        segs.append((x, y, x + r.normal() * 4, y + 40 + r.random() * 60))
    lines(cv, segs, 2.4, P.shadow_of(P.WOOD, 1.4), 0.5, clip=m)
    mo = m * smoothstep(0.58, 0.7, cv.noise(18, "mo", 3)) * smoothstep(560, 720, cv.xx() * 0 + 600 + (cv.xx() < 530) * 200)
    wash(cv, mo.astype(F32), P.MOSS, 0.8, pool=0.3, gran=0.2, key="tmo")
    # shimenawa rope with shide zigzag paper
    rope_y = 760
    rp = stroke(A([(392, rope_y - 14), (470, rope_y + 10), (540, rope_y + 14), (628, rope_y - 8)]), 26, 26, n=8)
    mr = cv.mask(rp)
    drop_shadow(cv, mr, 3, 8, 6, 0.4)
    paint(cv, mr, P.hx("e2c98a"), line=0.9, line_w=1.4, soft=8, hi=0.4)
    tw = []
    for k in range(14):
        x = 396 + k * 17
        yy = rope_y - 14 + (x - 392) * 0.1 if x < 470 else (rope_y + 10 if x < 540 else rope_y + 14 - (x - 540) * 0.25)
        tw.append((x - 6, yy - 12, x + 8, yy + 12))
    lines(cv, tw, 2.4, P.THATCH_DK, 0.6, clip=mr)
    for k, x in enumerate((430, 500, 570)):
        yy = rope_y + 14
        z = A([(x - 10, yy), (x + 10, yy), (x + 10, yy + 18), (x - 2, yy + 18), (x - 2, yy + 34), (x + 14, yy + 34), (x + 14, yy + 52),
               (x - 6, yy + 52), (x - 6, yy + 36), (x - 10, yy + 36)])
        part(cv, z, P.WHITE_WARM, line=0.8, line_w=1.0, soft=2, ao=0)
    cl = [(512, 320, 420, 230), (300, 260, 230, 170), (720, 260, 240, 170), (512, 150, 280, 150), (210, 420, 180, 120),
          (820, 410, 180, 120), (380, 440, 200, 120), (650, 440, 200, 120)]
    for (tx, ty) in tips:
        cl.append((tx, ty - 40, 170 + r.random() * 60, 120 + r.random() * 30))
    canopy(cv, cl, P.mix(P.MOSS, P.FOREST, 0.2), r, key + "cn")
    # small glowing spirit motes
    for i in range(10):
        x, y = 200 + r.random() * 620, 150 + r.random() * 420
        glow(cv, x, y, 14, P.LANTERN_CORE, 0.9, clip=True, falloff=1.4)
    grass_base(cv, 280, 760, 1004, r, key + "gb", h=40, n=6)
    return done(cv, 3.0)


def prop_tree_dead(key="prop_tree_dead"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 500, 140, 14, 0.35)
    polys = [stroke(A([(256, 504), (250, 400), (262, 300), (250, 220)]), 44, 18, n=8)]
    polys.append(catmull(A([(196, 506), (230, 470), (256, 440), (284, 470), (320, 506)]), True, 5))

    def branch(x, y, a, L, w, depth):
        ex, ey = x + math.cos(a) * L, y + math.sin(a) * L
        polys.append(stroke(A([(x, y), (x + math.cos(a + 0.2) * L * 0.5, y + math.sin(a + 0.2) * L * 0.5), (ex, ey)]), w, w * 0.35, n=5))
        if depth > 0:
            branch(ex, ey, a - 0.45 + r.normal() * 0.15, L * 0.68, w * 0.55, depth - 1)
            branch(ex, ey, a + 0.45 + r.normal() * 0.15, L * 0.62, w * 0.5, depth - 1)
    branch(252, 300, -math.pi / 2 - 0.5, 110, 20, 3)
    branch(256, 260, -math.pi / 2 + 0.55, 105, 18, 3)
    branch(250, 220, -math.pi / 2, 80, 14, 2)
    m = cv.polys_mask(polys)
    paint(cv, m, P.mix(P.BLIGHT, P.STONE_DK, 0.3), line=0.9, line_w=1.2, soft=10, key=key, tex=cv.noise((30, 3), "bk", 3), hi=0.3,
          lo=P.BLIGHT_DK)
    vm = m * smoothstep(0.6, 0.68, cv.noise((24, 4), "veins", 3))
    cv.atop(P.BLIGHT_VIOLET, vm * 0.6)
    crystal_cluster(cv, 210, 500, 40, r, key + "cr", n=3)
    return done(cv, 2.2)


def bush(cv, cx, by, w, h, r, key, col, berries=None):
    cl = [(cx, by - h * 0.55, w * 0.42, h * 0.36), (cx - w * 0.22, by - h * 0.32, w * 0.26, h * 0.26),
          (cx + w * 0.22, by - h * 0.32, w * 0.26, h * 0.26)]
    m = canopy(cv, cl, col, r, key, line=0.7, clump=0.42)
    # ground the bush: a dark band of low clumps along the base
    base = []
    for i in range(7):
        t = (i + 0.5) / 7
        base.append(blob(cx - w * 0.42 + t * w * 0.84, by - h * 0.1, w * 0.1, h * 0.12, r, 0.2, 7))
    bm0 = cv.polys_mask(base)
    paint(cv, bm0, P.mix(col, P.FOREST_DK, 0.55), line=0.7, line_w=1.0, soft=8, ao=0.3, hi=0.2)
    if berries:
        from brushes import bbox
        for i in range(int(w / 16)):
            a = r.random() * 6.28
            x, y = cx + math.cos(a) * w * 0.38 * math.sqrt(r.random()), by - h * 0.55 + math.sin(a) * h * 0.32 * math.sqrt(r.random())
            s = w * 0.022 + 2
            fl = [ellipse(x + math.cos(k * 1.257) * s * 0.6, y + math.sin(k * 1.257) * s * 0.6, s * 0.55, s * 0.4, k * 1.257, 10)
                  for k in range(5)]
            fm = cv.polys_mask(fl)
            paint(cv, fm, berries, line=0.6, line_w=0.8, soft=1.5, ao=0, hi=0.5)
            ell(cv, x, y, s * 0.3, s * 0.3, P.CREAM_WARM, line=0, soft=1, ao=0)
    return m


def prop_bush_a(key="prop_bush_a"):
    cv = new(key, 512, 256)
    r = rng(key)
    shadow_ellipse(cv, 262, 244, 200, 14, 0.3)
    bush(cv, 256, 246, 440, 220, r, key, P.mix(P.MOSS, P.SAGE, 0.3))
    return done(cv, 2.2)


def prop_bush_b(key="prop_bush_b"):
    cv = new(key, 512, 256)
    r = rng(key)
    shadow_ellipse(cv, 262, 244, 190, 14, 0.3)
    bush(cv, 256, 246, 400, 230, r, key, P.mix(P.MOSS_DK, P.MOSS, 0.5), berries=P.hx("f08aa0"))
    return done(cv, 2.2)


def boulder(cv, cx, by, w, h, r, key, moss=1.0, col=None):
    col = col or P.mix(P.STONE, P.VIOLET_FAR, 0.15)
    pts = []
    n = 14
    for i in range(n):
        a = math.pi + i / n * 2 * math.pi
        rr = 1.0 + (r.random() - 0.5) * 0.16
        x = cx + math.cos(a) * w * 0.5 * rr
        y = by - h * 0.5 + math.sin(a) * h * 0.5 * rr
        y = min(y, by)
        pts.append((x, y))
    m = cv.mask(catmull(A(pts), True, 6))
    m = warp(cv, m, w * 0.012, w * 0.15, key + "w")
    # facet planes
    paint(cv, m, col, line=0.8, line_w=1.6, key=key, hi=0.45, ao=0.4, var=0.05, var_cell=w * 0.3, cel=0.55, soft=w * 0.12)
    for k in range(3):
        x0 = cx + (r.random() - 0.5) * w * 0.5
        seg = [(x0, by - h * (0.7 + 0.2 * r.random())), (x0 + r.normal() * w * 0.05, by - h * 0.4), (x0 + r.normal() * w * 0.08, by - 4)]
        cm = cv.mask(stroke(A(seg), max(1.5, w * 0.008), 1.0, n=4)) * m
        cv.atop(P.shadow_of(col, 1.4), cm * 0.6)
    if moss > 0:
        Y = cv.yy()
        mm = m * smoothstep(by - h * (0.45 + 0.2 * cv.noise(w * 0.15, key + "mn", 3)), by - h * 0.8, Y)
        mm = ragged(cv, mm.astype(F32), 0.5, 1.5, 6, key + "mr")
        paint(cv, mm, P.MOSS, line=0.5, line_w=1.0, key=key + "m", soft=6, hi=0.5, ao=0, var=0.08, var_cell=12,
              light_col=P.GRASS_LIGHT)
    return m


def prop_rock_large(key="prop_rock_large"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 270, 492, 220, 20, 0.35)
    boulder(cv, 256, 494, 470, 360, r, key, moss=1.0)
    grass_base(cv, 50, 470, 494, r, key + "gb", h=40, n=5)
    return done(cv, 2.6)


def prop_rock_small(key="prop_rock_small"):
    cv = new(key, 256, 128)
    r = rng(key)
    shadow_ellipse(cv, 130, 120, 110, 8, 0.3)
    for i, (x, w, h) in enumerate(((100, 120, 100), (176, 80, 62), (40, 56, 40))):
        mossy_stone(cv, x, 122, w, h, r, key + str(i), moss=0.6)
    grass_base(cv, 10, 240, 122, r, key + "gb", h=18, n=3)
    return done(cv, 1.8)


def prop_stump(key="prop_stump"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 132, 240, 100, 10, 0.3)
    body = catmull(A([(40, 244), (62, 200), (66, 120), (190, 120), (194, 200), (216, 244)]), True, 6)
    paint(cv, cv.mask(body), P.WOOD, line=0.9, soft=14, tex=cv.noise((30, 3), "bk", 3), hi=0.3, ao=0.2)
    top = cv.ellipse_mask(128, 122, 62, 20)
    paint(cv, top, P.WOOD_LIGHT, line=0.9, soft=4, ao=0)
    for k in (44, 30, 16):
        rm = cv.ellipse_mask(128, 122, k, k * 0.32) - cv.ellipse_mask(128, 122, k - 2.2, k * 0.32 - 1.4)
        cv.atop(P.WOOD, np.clip(rm, 0, 1) * 0.7)
    mo = cv.a * smoothstep(0.6, 0.7, cv.noise(8, "mo", 3)) * smoothstep(150, 240, cv.yy())
    wash(cv, mo.astype(F32), P.MOSS, 0.85, pool=0.3, gran=0.2, key="mo")
    mushroom(cv, 200, 236, 22, P.hx("e8743b"), r)
    mushroom(cv, 222, 240, 14, P.hx("e8743b"), r)
    mushroom(cv, 52, 238, 16, P.CREAM_WARM, r)
    grass_base(cv, 30, 230, 244, r, key + "gb", h=22, n=3)
    return done(cv, 1.8)


def mushroom(cv, x, by, s, cap_col, r, glow_col=None, spots=True):
    stem = catmull(A([(x - s * 0.22, by), (x - s * 0.18, by - s * 0.9), (x + s * 0.18, by - s * 0.9), (x + s * 0.24, by)]), True, 4)
    part(cv, stem, P.CREAM, line=0.8, line_w=1.0, soft=s * 0.2, ao=0.2)
    cap = catmull(A([(x - s * 0.75, by - s * 0.78), (x - s * 0.5, by - s * 1.35), (x, by - s * 1.55), (x + s * 0.5, by - s * 1.35),
                     (x + s * 0.75, by - s * 0.78), (x, by - s * 0.92)]), True, 6)
    m = cv.mask(cap)
    if glow_col is not None:
        glow_pane(cv, m)
        cv.atop(glow_col, m * 0.55)
    else:
        paint(cv, m, cap_col, line=0.8, line_w=1.0, soft=s * 0.3, hi=0.6, gloss=0.4, ao=0)
    if spots:
        sp = [ellipse(x + (r.random() - 0.5) * s * 0.9, by - s * (1.05 + r.random() * 0.35), s * 0.09, s * 0.07, 0, 10) for _ in range(4)]
        cv.atop(P.WHITE_WARM, cv.polys_mask(sp) * m * 0.9)
    if glow_col is not None:
        glow(cv, x, by - s * 1.1, s * 2.0, glow_col, 0.35, clip=True)


def prop_log(key="prop_log"):
    cv = new(key, 512, 256)
    r = rng(key)
    shadow_ellipse(cv, 256, 238, 220, 12, 0.3)
    body = catmull(A([(40, 236), (36, 150), (60, 130), (440, 128), (470, 140), (476, 236)]), True, 6)
    m = cv.mask(body)
    paint(cv, m, P.WOOD, line=0.9, soft=30, tex=cv.noise((3, 40), "bk", 3), hi=0.3, ao=0.3)
    end = cv.ellipse_mask(452, 184, 40, 56)
    paint(cv, end, P.WOOD_LIGHT, line=0.9, soft=6, ao=0)
    for k in (30, 20, 10):
        rm = cv.ellipse_mask(452, 184, k, k * 1.4) - cv.ellipse_mask(452, 184, k - 2.2, k * 1.4 - 2.6)
        cv.atop(P.WOOD, np.clip(rm, 0, 1) * 0.7)
    mo = m * smoothstep(0.52, 0.64, cv.noise(10, "mo", 3)) * smoothstep(190, 130, cv.yy())
    mo = ragged(cv, mo.astype(F32), 0.3, 1, 3, "mor")
    paint(cv, mo, P.MOSS, line=0.5, line_w=0.8, soft=3, var=0.15, var_cell=5, hi=0.5, ao=0)
    fern(cv, 120, 236, 90, r, key + "f", P.MOSS)
    mushroom(cv, 300, 236, 18, P.CREAM_WARM, r)
    mushroom(cv, 322, 238, 12, P.CREAM_WARM, r)
    grass_base(cv, 30, 480, 238, r, key + "gb", h=22, n=5)
    return done(cv, 2.0)


def wheel(cv, cx, cy, rad, col=P.WOOD):
    outer = cv.ellipse_mask(cx, cy, rad, rad)
    inner = cv.ellipse_mask(cx, cy, rad * 0.8, rad * 0.8)
    rim = np.clip(outer - inner, 0, 1)
    spokes = cv.lines_mask([(cx + math.cos(a) * rad * 0.15, cy + math.sin(a) * rad * 0.15, cx + math.cos(a) * rad * 0.82,
                             cy + math.sin(a) * rad * 0.82) for a in np.linspace(0, 2 * math.pi, 9)[:-1]], max(3, rad * 0.1))
    m = np.maximum(rim, spokes)
    drop_shadow(cv, m, 2, 3, 3, 0.3)
    paint(cv, m.astype(F32), col, line=0.9, line_w=1.2, soft=4, ao=0, tex=wood_tex(cv, "wh"))
    ell(cv, cx, cy, rad * 0.2, rad * 0.2, P.WOOD_DK, line=0.8, soft=2, ao=0, hi=0.5)


def prop_cart(key="prop_cart"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 256, 490, 230, 16, 0.3)
    # far wheel
    wheel(cv, 340, 410, 70, P.WOOD_DK)
    # handles
    sline(cv, [(60, 400), (190, 360)], 12, P.WOOD, tex=wood_tex(cv, "h"))
    sline(cv, [(40, 420), (180, 380)], 12, P.WOOD, tex=wood_tex(cv, "h2"))
    # cargo: sacks, crate, pumpkins
    crate_at(cv, 300, 330, 90, r)
    sacks(cv, 220, 330, r)
    for i, x in enumerate((370, 420)):
        pm = cv.mask(catmull(A([(x - 30, 330), (x - 34, 300), (x - 14, 284), (x + 14, 284), (x + 34, 300), (x + 30, 330)]), True, 5))
        paint(cv, pm, P.hx("e8913b"), line=0.8, soft=8, hi=0.5)
        lines(cv, [(x - 12, 286, x - 14, 330), (x + 12, 286, x + 14, 330), (x, 284, x, 330)], 2, P.TERRACOTTA_DK, 0.6, clip=pm)
        sline(cv, [(x, 286), (x + 4, 274)], 5, P.MOSS_DK, line=0.4)
    # bed
    bed = A([(150, 330), (470, 330), (460, 400), (160, 400)])
    mb = cv.mask(bed)
    paint(cv, mb, P.WOOD_LIGHT, line=0.9, line_w=1.4, soft=12, tex=wood_tex(cv, "bed"))
    lines(cv, [(160, 354, 466, 354), (160, 378, 462, 378)], 2, P.WOOD_DK, 0.6, clip=mb)
    for x in (160, 300, 462):
        sline(cv, [(x, 290), (x, 400)], 10, P.WOOD, tex=wood_tex(cv, "p", True))
    wheel(cv, 250, 420, 76)
    grass_base(cv, 120, 480, 490, r, key + "gb", h=26, n=4)
    return done(cv, 2.2)


def crate_at(cv, cx, by, s, r):
    front = A([(cx - s / 2, by), (cx + s / 2, by), (cx + s / 2, by - s * 0.85), (cx - s / 2, by - s * 0.85)])
    side = A([(cx + s / 2, by), (cx + s * 0.72, by - s * 0.14), (cx + s * 0.72, by - s * 0.99), (cx + s / 2, by - s * 0.85)])
    top = A([(cx - s / 2, by - s * 0.85), (cx + s / 2, by - s * 0.85), (cx + s * 0.72, by - s * 0.99), (cx - s * 0.28, by - s * 0.99)])
    for poly, c in ((side, P.shadow_of(P.WOOD_LIGHT, 0.8)), (top, P.light_of(P.WOOD_LIGHT, 0.6)), (front, P.WOOD_LIGHT)):
        m = cv.mask(poly)
        paint(cv, m, c, line=0.9, line_w=1.2, soft=6, tex=wood_tex(cv, "cr"), ao=0.1)
    mf = cv.mask(front)
    lines(cv, [(cx - s / 2, by - s * k, cx + s / 2, by - s * k) for k in (0.28, 0.57)], 2, P.WOOD_DK, 0.6, clip=mf)
    lines(cv, [(cx - s / 2 + 6, by - 6, cx + s / 2 - 6, by - s * 0.85 + 6)], max(5, s * 0.08), P.WOOD, 1.0, clip=mf)
    lines(cv, [(cx - s / 2 + 6, by - 6, cx + s / 2 - 6, by - s * 0.85 + 6)], 1.5, P.WOOD_DK, 0.6, clip=mf)


def prop_barrel(key="prop_barrel"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 132, 244, 90, 10, 0.3)
    barrel_at(cv, 128, 246, 228, r)
    return done(cv, 1.8)


def prop_crate(key="prop_crate"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 136, 244, 100, 10, 0.3)
    crate_at(cv, 116, 246, 160, r)
    return done(cv, 1.8)


def prop_hay(key="prop_hay"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 132, 244, 110, 10, 0.3)
    body = catmull(A([(20, 244), (16, 120), (40, 70), (128, 54), (216, 70), (240, 120), (236, 244)]), True, 6)
    m = thatch(cv, body, key + "h", P.mix(P.THATCH, P.LEAF_YELLOW, 0.3), down=(0.1, 1), rows=0, bulge=0.02)
    for yy in (110, 190):
        band = stroke(A([(18, yy), (128, yy + 8), (238, yy)]), 9, 9, n=6)
        paint(cv, cv.mask(band) * m, P.TERRACOTTA_DK, line=0.6, soft=2, ao=0)
    return done(cv, 1.8)


def prop_signpost(key="prop_signpost"):
    cv = new(key, 256, 512)
    r = rng(key)
    shadow_ellipse(cv, 130, 498, 60, 9, 0.3)
    post = A([(116, 500), (118, 60), (126, 50), (136, 58), (140, 500)])
    part(cv, post, P.WOOD, line=0.9, soft=4, tex=wood_tex(cv, "p", True))
    for i, (y, d, c) in enumerate(((110, 1, P.WOOD_LIGHT), (190, -1, P.mix(P.WOOD_LIGHT, P.TERRACOTTA, 0.3)), (265, 1, P.WOOD_LIGHT))):
        x0 = 128
        if d > 0:
            pts = [(x0 - 40, y - 26), (x0 + 80, y - 26), (x0 + 110, y), (x0 + 80, y + 26), (x0 - 40, y + 26)]
        else:
            pts = [(x0 + 40, y - 26), (x0 - 80, y - 26), (x0 - 110, y), (x0 - 80, y + 26), (x0 + 40, y + 26)]
        m = cv.mask(A(pts))
        drop_shadow(cv, m, 3, 5, 4, 0.35)
        paint(cv, m, c, line=0.9, line_w=1.2, soft=6, tex=wood_tex(cv, "b%d" % i))
        # painted glyph dashes
        lines(cv, [(x0 - 20 * d + k * 18 * d, y - 4, x0 - 10 * d + k * 18 * d, y - 4) for k in range(4)], 4, P.INK_SOFT, 0.5,
              clip=m)
        lines(cv, [(x0 - 20 * d + k * 14 * d, y + 8, x0 - 12 * d + k * 14 * d, y + 8) for k in range(3)], 3, P.INK_SOFT, 0.4,
              clip=m)
        ell(cv, x0, y, 4, 4, P.INK_SOFT, line=0, soft=1, ao=0)
    grass_base(cv, 70, 190, 500, r, key + "gb", h=26, n=3)
    return done(cv, 2.0)


def prop_noticeboard(key="prop_noticeboard"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 496, 180, 14, 0.3)
    for x in (110, 402):
        sline(cv, [(x, 500), (x, 90)], 18, P.WOOD, tex=wood_tex(cv, "p", True))
    board = A([(96, 150), (416, 150), (416, 380), (96, 380)])
    mb = cv.mask(board)
    drop_shadow(cv, mb, 4, 6, 6, 0.35)
    paint(cv, mb, P.mix(P.WOOD, P.WOOD_LIGHT, 0.5), line=0.9, line_w=1.4, soft=20, tex=wood_tex(cv, "bd"))
    lines(cv, [(96, y, 416, y) for y in (196, 242, 288, 334)], 2, P.WOOD_DK, 0.5, clip=mb)
    papers = [(150, 210, 70, 90, -0.06, P.CREAM), (250, 200, 80, 70, 0.05, P.hx("fbe7c4")), (345, 230, 64, 84, -0.03, P.CREAM),
              (210, 300, 70, 56, 0.08, P.hx("e9eef6")), (320, 320, 60, 50, -0.08, P.CREAM_WARM)]
    for i, (x, y, w, h, a, c) in enumerate(papers):
        pts = rotate(A([(x - w / 2, y - h / 2), (x + w / 2, y - h / 2), (x + w / 2, y + h / 2), (x - w / 2, y + h / 2)]), a, x, y)
        m = cv.mask(pts)
        drop_shadow(cv, m, 2, 3, 3, 0.35)
        paint(cv, m, c, line=0.8, line_w=1.0, soft=6, ao=0.1, hi=0.2)
        segs = []
        for k in range(int(h / 14)):
            yy = y - h / 2 + 14 + k * 12
            segs.append((x - w / 2 + 10, yy, x + w / 2 - 10 - r.random() * 16, yy))
        lines(cv, segs, 2, P.INK_SOFT, 0.45, clip=m)
        ell(cv, x, y - h / 2 + 6, 4, 4, P.VERMILION, line=0.4, soft=1, ao=0)
    roof = A([(70, 152), (256, 70), (442, 152), (430, 166), (256, 92), (82, 166)])
    tile_roof(cv, roof, key + "rf", col=P.mix(P.WOOD, P.TERRACOTTA, 0.4), rows=4, tile_w=18)
    grass_base(cv, 80, 440, 500, r, key + "gb", h=26, n=4)
    return done(cv, 2.2)


def prop_bench(key="prop_bench"):
    cv = new(key, 512, 256)
    r = rng(key)
    shadow_ellipse(cv, 256, 238, 210, 12, 0.3)
    for x in (100, 412):
        sline(cv, [(x, 240), (x, 150)], 16, P.WOOD_DK, tex=wood_tex(cv, "l", True))
    back = A([(70, 60), (442, 60), (442, 110), (70, 110)])
    for x in (92, 420):
        sline(cv, [(x, 150), (x, 56)], 12, P.WOOD, tex=wood_tex(cv, "b", True))
    for y in (70, 102):
        sline(cv, [(64, y), (448, y)], 22, P.WOOD_LIGHT, tex=wood_tex(cv, "bk%d" % y))
    seat = A([(50, 140), (462, 140), (468, 168), (44, 168)])
    part(cv, seat, P.WOOD_LIGHT, line=0.9, line_w=1.2, soft=6, tex=wood_tex(cv, "s"))
    grass_base(cv, 40, 470, 240, r, key + "gb", h=24, n=5)
    return done(cv, 2.0)


def flame(cv, x, by, w, h, r, key, layers=3):
    cols = [P.EMBER, P.AMBER, P.LANTERN_CORE]
    for i in range(layers):
        s = 1.0 - i * 0.3
        tips = []
        n = 3
        for k in range(n):
            tx = x + (k - (n - 1) / 2) * w * 0.28 * s + r.normal() * w * 0.04
            th = h * s * (0.75 + 0.25 * (k == 1)) * (0.85 + 0.3 * r.random())
            tips.append((tx, by - th))
        pts = [(x - w * 0.5 * s, by)]
        for k, (tx, ty) in enumerate(tips):
            pts.append((tx - w * 0.12 * s, (ty + by) / 2 + h * 0.1))
            pts.append((tx, ty))
        pts.append((x + w * 0.5 * s, by))
        pts.append((x, by + h * 0.08))
        m = cv.mask(catmull(A(pts), True, 4))
        flat_fill(cv, m, cols[i], 0.95)


def prop_campfire(key="prop_campfire"):
    cv = new(key, 256, 256)
    r = rng(key)
    glow(cv, 128, 200, 120, P.AMBER, 0.0, clip=False)
    shadow_ellipse(cv, 128, 222, 100, 16, 0.3)
    # back stones
    for i in range(6):
        a = math.pi + i / 5 * math.pi
        x, y = 128 + math.cos(a) * 80, 214 + math.sin(a) * 20
        ell(cv, x, y, 22, 15, P.STONE, line=0.8, soft=5, hi=0.4)
    for k, (a, L) in enumerate(((-0.3, 140), (0.35, 130), (math.pi / 2, 70))):
        cx, cy = 128, 210
        s = stroke(A([(cx - math.cos(a) * L / 2, cy + math.sin(a) * 10 - 6), (cx + math.cos(a) * L / 2, cy - math.sin(a) * 10 - 6)]), 18, 16, n=2)
        part(cv, s, P.WOOD, line=0.9, soft=4, tex=wood_tex(cv, "lg%d" % k))
    flame(cv, 128, 206, 90, 140, r, key)
    glow(cv, 128, 200, 90, P.AMBER, 0.45, clip=True)
    for i in range(6):
        a = i / 5 * math.pi
        x, y = 128 + math.cos(a) * 84, 222 + math.sin(a) * 16
        ell(cv, x, y, 24, 17, P.STONE_WARM, line=0.8, soft=5, hi=0.4)
    sparks = [ellipse(128 + r.normal() * 30, 40 + r.random() * 50, 2.5, 2.5, 0, 8) for _ in range(6)]
    flat_fill(cv, cv.polys_mask(sparks), P.LANTERN, 0.9)
    return done(cv, 1.8)


def prop_tent(key="prop_tent"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 270, 492, 220, 18, 0.35)
    side = A([(256, 90), (470, 470), (500, 494), (330, 494)])
    part(cv, side, P.shadow_of(P.mix(P.CREAM_WARM, P.SAGE, 0.3), 0.9), line=0.9, soft=20, tex=cv.noise(10, "cv", 2) * 0.3 + 0.35)
    front = A([(256, 90), (40, 494), (330, 494)])
    mf = cv.mask(catmull(A([(256, 88), (150, 290), (40, 494), (190, 486), (330, 494), (296, 300)]), True, 4))
    paint(cv, mf, P.mix(P.CREAM_WARM, P.SAGE, 0.3), line=0.9, line_w=1.4, soft=30, tex=cv.noise(10, "cv2", 2) * 0.3 + 0.35)
    opening = catmull(A([(256, 180), (220, 330), (170, 494), (300, 494), (290, 330)]), True, 5)
    mo = cv.mask(opening)
    paint(cv, mo, P.mix(P.INK, P.WOOD_DK, 0.4), line=0.5, soft=10, ao=0, hi=0)
    glow(cv, 240, 440, 80, P.LANTERN, 0.35, clip=True)
    flap = catmull(A([(256, 180), (150, 400), (196, 494), (232, 360)]), True, 4)
    part(cv, flap, P.light_of(P.mix(P.CREAM_WARM, P.SAGE, 0.3), 0.6), line=0.9, soft=10)
    # patch + bunting
    part(cv, [(320, 300), (360, 296), (364, 336), (322, 340)], P.TERRACOTTA, line=0.8, soft=3)
    sline(cv, [(256, 92), (256, 50)], 8, P.WOOD, line=0.6)
    flagp = A([(256, 52), (300, 62), (256, 76)])
    part(cv, flagp, P.VERMILION, line=0.8, soft=3)
    for x in (40, 500):
        sline(cv, [(x, 494), (x + (256 - x) * 0.05, 470)], 6, P.WOOD, line=0.4)
    grass_base(cv, 30, 500, 494, r, key + "gb", h=26, n=5)
    return done(cv, 2.2)


def chest(cv, cx, by, s, r, opened=False):
    W, H = s, s * 0.55
    body = A([(cx - W / 2, by), (cx + W / 2, by), (cx + W / 2, by - H), (cx - W / 2, by - H)])
    side = A([(cx + W / 2, by), (cx + W * 0.66, by - H * 0.22), (cx + W * 0.66, by - H * 1.2), (cx + W / 2, by - H)])
    part(cv, side, P.shadow_of(P.mix(P.WOOD, P.TERRACOTTA, 0.3), 0.9), line=0.9, soft=6, tex=wood_tex(cv, "cs"))
    if opened:
        inside = A([(cx - W / 2, by - H), (cx + W / 2, by - H), (cx + W * 0.66, by - H * 1.2), (cx - W * 0.34, by - H * 1.2)])
        part(cv, inside, P.mix(P.INK, P.WOOD_DK, 0.4), line=0.5, soft=4, ao=0, hi=0)
        coins = []
        for i in range(14):
            coins.append(ellipse(cx - W * 0.3 + r.random() * W * 0.8, by - H * 1.05 - r.random() * H * 0.25, s * 0.06, s * 0.035, 0, 12))
        mc = cv.polys_mask(coins)
        paint(cv, mc, P.HONEY, line=0.7, line_w=0.8, soft=2, hi=0.8, gloss=0.6, ao=0)
        glow(cv, cx, by - H * 1.3, s * 0.9, P.LANTERN, 0.5, clip=False, falloff=2.5)
        lid = A([(cx - W * 0.5, by - H * 1.22), (cx + W * 0.66, by - H * 1.42), (cx + W * 0.6, by - H * 2.0), (cx + W * 0.45, by - H * 2.12),
                 (cx - W * 0.42, by - H * 1.92), (cx - W * 0.56, by - H * 1.7)])
        part(cv, lid, P.mix(P.WOOD, P.TERRACOTTA, 0.3), line=0.9, soft=8, tex=wood_tex(cv, "lid"), hi=0.4)
    mb = cv.mask(body)
    paint(cv, mb, P.mix(P.WOOD, P.TERRACOTTA, 0.3), line=0.9, line_w=1.2, soft=8, tex=wood_tex(cv, "cb"))
    if not opened:
        lid_side = A([(cx + W / 2, by - H), (cx + W * 0.66, by - H * 1.22), (cx + W * 0.66, by - H * 1.45), (cx + W / 2, by - H * 1.42)])
        part(cv, lid_side, P.shadow_of(P.mix(P.WOOD, P.TERRACOTTA, 0.3), 0.8), line=0.9, soft=4, tex=wood_tex(cv, "ls"))
        arc = [(cx - W / 2 + t * W, by - H - H * 0.42 - math.sin(math.pi * t) * H * 0.22) for t in np.linspace(0, 1, 10)]
        lid = A([(cx - W / 2 - 2, by - H + 2)] + arc + [(cx + W / 2 + 2, by - H + 2)])
        part(cv, lid, P.mix(P.WOOD, P.TERRACOTTA, 0.3), line=0.9, soft=10, tex=wood_tex(cv, "lid"), hi=0.4)
        top = A([(cx + W / 2, by - H * 1.42)] + [(cx - W / 2 + t * W + W * 0.16, by - H - H * 0.62 - math.sin(math.pi * t) * H * 0.22)
                                               for t in np.linspace(1, 0, 10)][:1] + [(cx + W * 0.66, by - H * 1.45)])
    for fx in (-0.3, 0.3):
        band = A([(cx + W * fx - s * 0.04, by), (cx + W * fx + s * 0.04, by), (cx + W * fx + s * 0.04, by - H * (1.0 if opened else 1.6)),
                  (cx + W * fx - s * 0.04, by - H * (1.0 if opened else 1.6))])
        part(cv, band, P.HONEY, line=0.8, soft=2, hi=0.6, gloss=0.4, ao=0)
    lock = A([(cx - s * 0.06, by - H * 0.95), (cx + s * 0.06, by - H * 0.95), (cx + s * 0.06, by - H * 0.65), (cx - s * 0.06, by - H * 0.65)])
    part(cv, lock, P.HONEY, line=0.8, soft=2, hi=0.6, gloss=0.5, ao=0)


def prop_chest(key="prop_chest"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 136, 232, 100, 10, 0.3)
    chest(cv, 120, 234, 170, r)
    return done(cv, 1.8)


def prop_chest_open(key="prop_chest_open"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 136, 232, 100, 10, 0.3)
    chest(cv, 120, 234, 170, r, opened=True)
    return done(cv, 1.8)


def prop_mushrooms(key="prop_mushrooms"):
    cv = new(key, 256, 256)
    r = rng(key)
    shadow_ellipse(cv, 128, 236, 90, 10, 0.25)
    gc = P.hx("8fe3d6")
    for (x, s) in sorted(((80, 50), (140, 72), (190, 44), (110, 34), (168, 28), (60, 26)), key=lambda t: t[1]):
        mushroom(cv, x, 238 - (72 - s) * 0.1, s, P.hx("6fc9c0"), r, glow_col=gc, spots=True)
    grass_base(cv, 30, 226, 240, r, key + "gb", h=20, n=3)
    return done(cv, 1.8)


def prop_ruin_pillar(key="prop_ruin_pillar"):
    cv = new(key, 256, 512)
    r = rng(key)
    shadow_ellipse(cv, 134, 498, 90, 10, 0.3)
    base = A([(50, 500), (50, 460), (206, 460), (206, 500)])
    part(cv, base, P.STONE_WARM, line=0.9, soft=6, hi=0.4)
    col = A([(72, 462), (74, 150), (96, 140), (110, 120), (126, 138), (150, 110), (170, 130), (182, 150), (184, 462)])
    m = cv.mask(col)
    paint(cv, m, P.STONE_WARM, line=0.9, line_w=1.4, soft=30, hi=0.45, ao=0.2, var=0.1, var_cell=16)
    flutes = [(x, 150, x, 460) for x in (96, 118, 140, 162)]
    lines(cv, flutes, 3, P.shadow_of(P.STONE_WARM, 1.2), 0.5, clip=m)
    for yy in (250, 360):
        lines(cv, [(72, yy, 184, yy + 4)], 2.5, P.shadow_of(P.STONE_WARM, 1.5), 0.7, clip=m)
    moss_on(cv, key, cell=40, thr=0.58, top_bias=smoothstep(260, 120, cv.yy()) * 0.25)
    ivy(cv, [(184, 440), (176, 330), (186, 220)], r, key + "iv", 8, 40)
    rubble = [(30, 500, 30, 22), (220, 502, 26, 18)]
    for (x, y, w, h) in rubble:
        ell(cv, x, y - h / 2, w, h / 2 + 4, P.STONE, line=0.9, soft=4, hi=0.4)
    grass_base(cv, 30, 226, 502, r, key + "gb", h=24, n=4)
    return done(cv, 2.0)


def prop_ruin_arch(key="prop_ruin_arch"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 498, 220, 14, 0.3)
    outer = []
    # left column + arch blocks (broken on the right)
    blocks = []
    for k in range(7):
        y = 500 - k * 44
        blocks.append(A([(70, y), (140, y), (140, y - 42), (70, y - 42)]))
    for k in range(4):
        y = 500 - k * 44
        blocks.append(A([(372, y), (442, y), (442, y - 42), (372, y - 42)]))
    # voussoirs
    cx, cy, R0, R1 = 256, 192, 116, 186
    for k in range(9):
        a0 = math.pi + k / 9 * math.pi
        a1 = math.pi + (k + 1) / 9 * math.pi
        if k >= 7:
            continue
        pts = [(cx + math.cos(a0) * R0, cy + math.sin(a0) * R0), (cx + math.cos(a0) * R1, cy + math.sin(a0) * R1),
               (cx + math.cos(a1) * R1, cy + math.sin(a1) * R1), (cx + math.cos(a1) * R0, cy + math.sin(a1) * R0)]
        blocks.append(A(pts))
    for i, b in enumerate(blocks):
        c = b.mean(0)
        sh = (b - c) * 0.94 + c
        m = cv.mask(sh)
        paint(cv, m, P.mix(P.STONE_WARM, P.STONE, r.random() * 0.6), line=0.9, line_w=1.2, soft=8, key=key + str(i), hi=0.45,
              var=0.1, var_cell=10, ao=0.15)
    moss_on(cv, key, cell=46, thr=0.6, top_bias=smoothstep(200, 40, cv.yy()) * 0.2)
    ivy(cv, [(140, 140), (160, 70), (240, 20), (330, 40)], r, key + "iv", 9, 70)
    for (x, y, w, h) in ((440, 500, 40, 30), (480, 498, 26, 20), (360, 502, 22, 16)):
        part(cv, rotate(A([(x - w, y), (x + w, y), (x + w * 0.8, y - h), (x - w * 0.9, y - h)]), r.normal() * 0.2, x, y), P.STONE_WARM,
             line=0.9, soft=4, hi=0.4)
    grass_base(cv, 40, 480, 502, r, key + "gb", h=28, n=6)
    return done(cv, 2.2)


def prop_shrine_gate(key="prop_shrine_gate"):
    cv = new(key, 1024, 1024)
    r = rng(key)
    from environment import torii
    shadow_ellipse(cv, 520, 1000, 400, 24, 0.3)
    torii(cv, 512, 1000, 960, key, col=P.VERMILION, faded=0.0, line=0.9, lw=1.6)
    # stone bases & black post feet
    for sx in (-1, 1):
        px = 512 + sx * 960 * 1.05 * 0.33
        part(cv, [(px - 52, 1000), (px - 46, 930), (px + 46, 930), (px + 52, 1000)], P.INK_SOFT, line=0.8, soft=6, hi=0.4)
    # plaque
    pl = A([(470, 150), (554, 150), (554, 260), (470, 260)])
    part(cv, pl, P.INK_SOFT, line=0.9, soft=6, hi=0.4)
    part(cv, [(482, 162), (542, 162), (542, 248), (482, 248)], P.HONEY, line=0.6, soft=3, hi=0.5, gloss=0.3)
    lines(cv, [(512, 176, 512, 236), (496, 190, 528, 190), (496, 214, 528, 214)], 5, P.INK_SOFT, 0.8)
    # shimenawa rope between posts with shide
    rp = stroke(A([(300, 330), (512, 360), (724, 330)]), 18, 18, n=8)
    paint(cv, cv.mask(rp), P.hx("e2c98a"), line=0.9, soft=6, hi=0.4)
    for x in (380, 512, 644):
        yy = 352 if x == 512 else 345
        z = A([(x - 10, yy), (x + 10, yy), (x + 10, yy + 22), (x - 2, yy + 22), (x - 2, yy + 42), (x + 14, yy + 42), (x + 14, yy + 64),
               (x - 6, yy + 64), (x - 6, yy + 44), (x - 10, yy + 44)])
        part(cv, z, P.WHITE_WARM, line=0.8, soft=2, ao=0)
    mo = cv.a * smoothstep(0.62, 0.7, cv.noise(10, "mo", 3)) * smoothstep(120, 60, cv.yy() * 0 + 100)
    grass_base(cv, 240, 790, 1002, r, key + "gb", h=40, n=6)
    return done(cv, 3.0)


def prop_spirit_statue(key="prop_spirit_statue"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 496, 150, 14, 0.3)
    stone = P.mix(P.STONE, P.STONE_WARM, 0.5)
    ped = A([(150, 500), (156, 400), (356, 400), (362, 500)])
    part(cv, ped, stone, line=0.9, soft=14, hi=0.4, var=0.1)
    part(cv, [(136, 410), (376, 410), (370, 384), (142, 384)], stone, line=0.9, soft=4, hi=0.4)
    # sitting fox: tail, body, head with tall ears
    tail = catmull(A([(300, 384), (360, 360), (380, 300), (350, 250), (330, 300), (316, 350)]), True, 6)
    part(cv, tail, stone, line=0.9, soft=12, hi=0.45)
    body = catmull(A([(196, 386), (190, 300), (216, 230), (256, 214), (296, 230), (316, 300), (312, 386)]), True, 6)
    part(cv, body, stone, line=0.9, line_w=1.4, soft=24, hi=0.45, var=0.1)
    for sx in (-1, 1):
        leg = A([(256 + sx * 30, 386), (256 + sx * 22, 300), (256 + sx * 40, 300), (256 + sx * 46, 386)])
        part(cv, leg, P.light_of(stone, 0.3), line=0.8, soft=6, hi=0.4)
    head = catmull(A([(214, 200), (220, 150), (256, 130), (292, 150), (298, 200), (256, 236)]), True, 6)
    part(cv, head, stone, line=0.9, soft=14, hi=0.45)
    for sx in (-1, 1):
        ear = A([(256 + sx * 22, 150), (256 + sx * 34, 70), (256 + sx * 48, 160)])
        part(cv, ear, stone, line=0.9, soft=4, hi=0.4)
    snout = catmull(A([(238, 200), (256, 232), (274, 200), (256, 190)]), True, 4)
    part(cv, snout, P.light_of(stone, 0.4), line=0.6, soft=4, ao=0)
    for sx in (-1, 1):
        sline(cv, [(256 + sx * 22, 182), (256 + sx * 12, 186)], 4, P.INK_SOFT, line=0)
    # red bib
    bib = catmull(A([(214, 236), (298, 236), (290, 262), (256, 296), (222, 262)]), True, 5)
    part(cv, bib, P.VERMILION, line=0.9, soft=6, hi=0.4, tex=cv.noise(4, "cl", 2) * 0.3 + 0.35)
    moss_on(cv, key, cell=40, thr=0.62, where=smoothstep(380, 420, cv.yy()) + smoothstep(170, 120, cv.yy()))
    # offering: small lantern glow + flowers
    glow(cv, 256, 330, 120, P.LANTERN, 0.15, clip=True)
    grass_base(cv, 120, 400, 500, r, key + "gb", h=26, n=4)
    return done(cv, 2.4)


def prop_blight_crystal(key="prop_blight_crystal"):
    cv = new(key, 512, 512)
    r = rng(key)
    shadow_ellipse(cv, 262, 492, 180, 16, 0.35)
    stain = cv.blur(cv.ellipse_mask(256, 492, 200, 22), 8)
    flat_fill(cv, stain, P.BLIGHT_DK, 0.6)
    crystal_cluster(cv, 256, 494, 300, r, key + "a", n=5, glow_amt=0.8)
    crystal_cluster(cv, 120, 498, 140, r, key + "b", n=3)
    crystal_cluster(cv, 392, 498, 160, r, key + "c", n=3)
    return done(cv, 2.4)


def prop_banner(key="prop_banner"):
    cv = new(key, 256, 512)
    r = rng(key)
    shadow_ellipse(cv, 130, 500, 50, 8, 0.3)
    sline(cv, [(70, 502), (70, 30)], 12, P.WOOD, tex=wood_tex(cv, "p", True))
    sline(cv, [(60, 70), (220, 70)], 8, P.WOOD_DK, line=0.6)
    ell(cv, 70, 26, 10, 10, P.HONEY, line=0.8, soft=2, hi=0.6)
    cloth = catmull(A([(76, 76), (212, 76), (214, 260), (200, 330), (170, 300), (144, 340), (116, 300), (84, 330), (78, 250)]), True, 5)
    m = cv.mask(cloth)
    drop_shadow(cv, m, 4, 6, 5, 0.35)
    paint(cv, m, P.mix(P.ROOF_BLUE, P.DUSTY_BLUE_DK, 0.4), line=0.9, line_w=1.4, soft=20, tex=cv.noise((40, 6), "fold", 2) * 0.5 + 0.25,
          hi=0.4)
    trim = cv.mask(stroke(A([(76, 92), (212, 92)]), 8, 8, n=2)) * m
    paint(cv, trim.astype(F32), P.HONEY, line=0, soft=2, ao=0)
    # lantern emblem
    em = A([(130, 150), (160, 150), (168, 210), (122, 210)])
    glow_pane(cv, cv.mask(em))
    part(cv, [(124, 150), (145, 130), (166, 150)], P.HONEY, line=0.7, soft=2, ao=0)
    part(cv, [(120, 210), (170, 210), (166, 220), (124, 220)], P.HONEY, line=0.7, soft=2, ao=0)
    grass_base(cv, 30, 120, 502, r, key + "gb", h=22, n=2)
    return done(cv, 2.0)


def prop_bridge(key="prop_bridge"):
    cv = new(key, 1024, 512)
    r = rng(key)
    shadow_ellipse(cv, 512, 480, 440, 18, 0.3)
    # arched deck seen from the side
    top_arc = [(40 + t * 944, 400 - math.sin(math.pi * t) * 150) for t in np.linspace(0, 1, 20)]
    bot_arc = [(80 + t * 864, 452 - math.sin(math.pi * t) * 150) for t in np.linspace(1, 0, 20)]
    deck = A(top_arc + bot_arc)
    md = cv.mask(deck)
    drop_shadow(cv, md, 4, 10, 8, 0.4)
    paint(cv, md, P.WOOD, line=0.9, line_w=1.4, soft=20, tex=wood_tex(cv, "d"))
    planks = []
    for t in np.linspace(0.02, 0.98, 30):
        x = 40 + t * 944
        y = 400 - math.sin(math.pi * t) * 150
        planks.append((x, y, x + 20, y + 52))
    lines(cv, planks, 2, P.WOOD_DK, 0.5, clip=md)
    # posts + railing
    posts = []
    for t in np.linspace(0.04, 0.96, 7):
        x = 40 + t * 944
        y = 400 - math.sin(math.pi * t) * 150
        posts.append(stroke(A([(x, y + 10), (x, y - 90)]), 18, 16, n=2, cap=False))
        posts.append(ellipse(x, y - 94, 12, 10, 0, 16))
    rail = stroke(A([(40 + t * 944, 400 - math.sin(math.pi * t) * 150 - 80) for t in np.linspace(0.04, 0.96, 12)]), 14, 14, n=4)
    m = cv.polys_mask(posts + [rail])
    drop_shadow(cv, m, 3, 6, 5, 0.3)
    paint(cv, m, P.mix(P.VERMILION, P.WOOD, 0.35), line=0.9, line_w=1.2, soft=6, hi=0.4)
    for x in (60, 960):
        grass_tuft(cv, x, 470, 60, 120, r, key + "gt%d" % x, n=14)
    return done(cv, 2.6)


# ----------------------------------------------------------------------------------------
# registration
# ----------------------------------------------------------------------------------------
PROPS = {
    # key: (fn, (w,h), catalogue height m)
    "prop_cottage_a": (prop_cottage_a, (1024, 1024), 6.0),
    "prop_cottage_b": (prop_cottage_b, (1024, 1024), 7.0),
    "prop_cottage_c": (prop_cottage_c, (1024, 1024), 6.0),
    "prop_inn": (prop_inn, (1024, 1024), 8.0),
    "prop_shop_stall": (prop_shop_stall, (512, 512), 3.2),
    "prop_smithy": (prop_smithy, (1024, 512), 4.5),
    "prop_windmill": (prop_windmill, (512, 1024), 9.0),
    "prop_well": (prop_well, (512, 512), 2.6),
    "prop_fence": (prop_fence, (512, 256), 1.1),
    "prop_lamp_post": (prop_lamp_post, (256, 512), 2.8),
    "prop_spirit_lantern": (prop_spirit_lantern, (256, 512), 2.4),
    "prop_spirit_lantern_dark": (prop_spirit_lantern_dark, (256, 512), 2.4),
    "prop_tree_oak": (prop_tree_oak, (1024, 1024), 7.0),
    "prop_tree_pine": (prop_tree_pine, (512, 1024), 9.0),
    "prop_tree_birch": (prop_tree_birch, (512, 1024), 7.0),
    "prop_tree_great": (prop_tree_great, (1024, 1024), 12.0),
    "prop_tree_dead": (prop_tree_dead, (512, 512), 6.0),
    "prop_bush_a": (prop_bush_a, (512, 256), 1.4),
    "prop_bush_b": (prop_bush_b, (512, 256), 1.4),
    "prop_rock_large": (prop_rock_large, (512, 512), 2.2),
    "prop_rock_small": (prop_rock_small, (256, 128), 0.8),
    "prop_stump": (prop_stump, (256, 256), 0.9),
    "prop_log": (prop_log, (512, 256), 1.0),
    "prop_cart": (prop_cart, (512, 512), 2.0),
    "prop_barrel": (prop_barrel, (256, 256), 1.1),
    "prop_crate": (prop_crate, (256, 256), 1.0),
    "prop_hay": (prop_hay, (256, 256), 1.3),
    "prop_signpost": (prop_signpost, (256, 512), 2.0),
    "prop_noticeboard": (prop_noticeboard, (512, 512), 2.4),
    "prop_bench": (prop_bench, (512, 256), 0.9),
    "prop_campfire": (prop_campfire, (256, 256), 1.0),
    "prop_tent": (prop_tent, (512, 512), 2.4),
    "prop_chest": (prop_chest, (256, 256), 0.9),
    "prop_chest_open": (prop_chest_open, (256, 256), 0.9),
    "prop_mushrooms": (prop_mushrooms, (256, 256), 0.7),
    "prop_ruin_pillar": (prop_ruin_pillar, (256, 512), 3.5),
    "prop_ruin_arch": (prop_ruin_arch, (512, 512), 5.0),
    "prop_shrine_gate": (prop_shrine_gate, (1024, 1024), 6.0),
    "prop_spirit_statue": (prop_spirit_statue, (512, 512), 1.8),
    "prop_blight_crystal": (prop_blight_crystal, (512, 512), 2.0),
    "prop_banner": (prop_banner, (256, 512), 3.0),
    "prop_bridge": (prop_bridge, (1024, 512), 2.0),
}

NO_SHADOW = {"prop_bridge"}


def _one(fn, key):
    return {key: fn(key)}


def register(reg):
    for k, (fn, size, h) in PROPS.items():
        # pivot: bottom of the painted base line (props stand ~3-6% above the image bottom)
        piv_y = {256: 0.05, 128: 0.05, 512: 0.035, 1024: 0.035}[size[1]]
        reg.spec(k, "Prop", size=size, measure=h, pivot=(0.5, piv_y), shadow=k not in NO_SHADOW)
        reg.job([k], _one, fn, k)
