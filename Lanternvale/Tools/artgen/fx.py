"""Effects: white / greyscale sprites so the engine can tint them by school colour.

Transparent pixels carry white RGB so bilinear filtering never produces dark fringes.
fx_shadow is the exception: it is a soft dark ellipse (multiply it with any tint and it stays dark).
"""
import math

import numpy as np
from PIL import Image

from brushes import (Canvas, F32, blur, catmull, ellipse, rng, smoothstep, stroke)
import palette as P


def to_white(alpha, lum=None, rgb=None):
    """alpha (h,w) 0..1, optional lum (h,w) 0..1 greyscale -> RGBA image with white transparent pixels."""
    a = np.clip(alpha, 0, 1)
    if rgb is None:
        l_ = np.ones_like(a) if lum is None else np.clip(lum, 0, 1)
        rgb = np.dstack([l_, l_, l_])
    rgb = np.where((a <= 1.0 / 255)[..., None], 1.0, rgb)
    out = np.dstack([np.round(rgb * 255), np.round(a * 255)]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def grid(n):
    y, x = np.mgrid[0:n, 0:n].astype(F32)
    return (x + 0.5) / n * 2 - 1, (y + 0.5) / n * 2 - 1


def fx_glow(key="fx_glow"):
    x, y = grid(256)
    d = np.sqrt(x * x + y * y)
    a = np.exp(-(d / 0.42) ** 2) * smoothstep(1.0, 0.85, d)
    return to_white(a)


def fx_spark(key="fx_spark"):
    n = 256
    x, y = grid(n)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    star = np.clip(1.0 - d / (0.12 + 0.85 * np.abs(np.cos(2 * ang)) ** 18), 0, 1) ** 1.5
    star2 = np.clip(1.0 - d / (0.08 + 0.45 * np.abs(np.cos(2 * ang + math.pi / 4)) ** 24), 0, 1) ** 1.5 * 0.6
    halo = np.exp(-(d / 0.25) ** 2) * 0.7
    a = np.clip(star + star2 + halo, 0, 1) * smoothstep(1.0, 0.9, d)
    return to_white(a)


def fx_ring(key="fx_ring"):
    n = 512
    x, y = grid(n)
    d = np.sqrt(x * x + y * y)
    a = np.exp(-((d - 0.86) / 0.035) ** 2) + np.exp(-((d - 0.86) / 0.11) ** 2) * 0.25
    return to_white(np.clip(a, 0, 1))


def fx_bolt(key="fx_bolt"):
    """Glowing projectile flying right: hot core, soft halo, tapering tail."""
    w, h = 512, 256
    y, x = np.mgrid[0:h, 0:w].astype(F32)
    cx, cy = 380.0, 128.0
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
    core = np.exp(-(d / 34) ** 2)
    halo = np.exp(-(d / 80) ** 2) * 0.5
    t = np.clip((cx - x) / 340.0, 0, 1)
    tail_w = 40 * (1 - t) ** 1.2 + 2
    tail = np.exp(-((y - cy) / tail_w) ** 2) * (1 - t) ** 1.4 * (x < cx)
    r = rng(key)
    streaks = np.zeros_like(x)
    for k in range(5):
        off = r.normal() * 14
        streaks += np.exp(-((y - cy - off * (1 - t)) / 3.0) ** 2) * (1 - t) ** 2 * (x < cx - 20) * 0.35
    a = np.clip(core + halo + tail * 0.8 + streaks, 0, 1)
    lum = np.clip(0.75 + 0.25 * (core + tail * 0.3), 0, 1)
    return to_white(a, lum)


def fx_arrow(key="fx_arrow"):
    cv = Canvas(512, 128, 2.0, seed=key)
    shaft = stroke(np.array([(40, 64), (430, 64)]), 7, 7, n=2)
    head = np.array([(420, 46), (500, 64), (420, 82), (432, 64)])
    fl1 = np.array([(30, 64), (70, 40), (120, 40), (95, 64)])
    fl2 = np.array([(30, 64), (70, 88), (120, 88), (95, 64)])
    m_sh = cv.mask(shaft)
    m_hd = cv.mask(head)
    m_fl = cv.polys_mask([fl1, fl2])
    a = np.clip(m_sh + m_hd + m_fl, 0, 1)
    lum = 0.78 * m_sh + 0.62 * m_hd + 0.98 * m_fl
    lum = lum + (1 - a) * 1.0
    yy = cv.yy()
    lum = lum * (1.0 - 0.12 * smoothstep(64, 90, yy))
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((512, 128), Image.BOX), F32) / 255
    lum = np.asarray(Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8)).resize((512, 128), Image.BOX), F32) / 255
    return to_white(a, lum)


def fx_slash(key="fx_slash"):
    n = 512
    y, x = np.mgrid[0:n, 0:n].astype(F32)
    cx, cy = 210.0, 300.0
    R = 210.0
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
    ang = np.arctan2(y - cy, x - cx)
    a0, a1 = -2.4, 0.9
    t = np.clip((ang - a0) / (a1 - a0), 0, 1)
    inside = (ang > a0) & (ang < a1)
    width = 6 + 46 * np.sin(np.pi * t) ** 0.8 * (0.3 + 0.7 * t)
    band = np.exp(-((d - R) / np.maximum(width, 1)) ** 4) * inside
    soft = np.exp(-((d - R + 10) / (width * 2.2 + 4)) ** 2) * inside * 0.35
    a = np.clip(band * (0.25 + 0.75 * t) + soft * t, 0, 1)
    return to_white(a)


def plus_mask(x, y, cx, cy, s, w):
    return ((np.abs(x - cx) < w) & (np.abs(y - cy) < s)) | ((np.abs(y - cy) < w) & (np.abs(x - cx) < s))


def fx_heal(key="fx_heal"):
    w, h = 256, 512
    big = 4
    W, H = w * big, h * big
    y, x = np.mgrid[0:H, 0:W].astype(F32) / big
    a = np.zeros_like(x)
    r = rng(key)
    items = [(128, 330, 46, 15, 1.0), (70, 220, 28, 9, 0.8), (190, 160, 24, 8, 0.7), (110, 90, 18, 6, 0.55), (180, 420, 20, 7, 0.65)]
    for (cx, cy, s, ww, k) in items:
        a = np.maximum(a, plus_mask(x, y, cx, cy, s, ww).astype(F32) * k)
    img = Image.fromarray((a * 255).astype(np.uint8)).resize((w, h), Image.BOX)
    a = np.asarray(img, F32) / 255
    a = np.maximum(a, blur(a, 6) * 0.9)
    yy, xx = np.mgrid[0:h, 0:w].astype(F32)
    for i in range(12):
        sx, sy = r.random() * w, r.random() * h
        rr = 2 + r.random() * 4
        a = np.maximum(a, np.exp(-(((xx - sx) ** 2 + (yy - sy) ** 2) / (rr * rr))) * 0.9)
    fade = smoothstep(0, 140, yy)
    return to_white(np.clip(a * fade, 0, 1))


def fx_smoke(key="fx_smoke"):
    cv = Canvas(256, 256, 1.0, seed=key)
    r = rng(key)
    a = np.zeros((256, 256), F32)
    lum = np.zeros((256, 256), F32)
    for i in range(9):
        ang = r.random() * 6.28
        d = r.random() * 50
        cx, cy = 128 + math.cos(ang) * d, 135 + math.sin(ang) * d * 0.8
        rad = 40 + r.random() * 35
        m = cv.ellipse_mask(cx, cy, rad, rad * 0.9)
        m = blur(m, 6)
        top = smoothstep(cy + rad, cy - rad, cv.yy())
        a = np.maximum(a, m)
        lum = np.maximum(lum, m * (0.65 + 0.35 * top))
    n = cv.noise(30, "n", 3)
    a = np.clip(a * (0.75 + 0.5 * (n - 0.5) * 2) * 0.85, 0, 1)
    lum = np.clip(lum / np.maximum(a, 1e-3) * 0.85, 0.55, 1.0)
    return to_white(a, lum)


def fx_leaf(key="fx_leaf"):
    cv = Canvas(128, 128, 4.0, seed=key)
    from environment import leaf_poly
    m = cv.mask(leaf_poly(18, 100, 100, -0.75, 0.42))
    stem = cv.mask(stroke(np.array([(10, 112), (22, 98)]), 3.5, 2.5, n=2))
    vein = cv.lines_mask([(22, 96, 88, 30)] + [(30 + k * 12, 88 - k * 12, 42 + k * 12, 96 - k * 12) for k in range(5)]
                         + [(30 + k * 12, 88 - k * 12, 24 + k * 12, 76 - k * 12) for k in range(5)], 1.6)
    a = np.clip(m + stem, 0, 1)
    lum = 0.95 - 0.25 * vein * m - 0.12 * smoothstep(40, 110, cv.xx() + cv.yy() * 0) * 0
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((128, 128), Image.BOX), F32) / 255
    lum = np.asarray(Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8)).resize((128, 128), Image.BOX), F32) / 255
    return to_white(a, lum)


def fx_firefly(key="fx_firefly"):
    x, y = grid(128)
    d = np.sqrt(x * x + y * y)
    a = np.clip(np.exp(-(d / 0.1) ** 2) + np.exp(-(d / 0.45) ** 2) * 0.45, 0, 1) * smoothstep(1.0, 0.85, d)
    return to_white(a)


def fx_shadow(key="fx_shadow"):
    w, h = 256, 128
    y, x = np.mgrid[0:h, 0:w].astype(F32)
    nx = (x + 0.5 - w / 2) / (w * 0.46)
    ny = (y + 0.5 - h / 2) / (h * 0.42)
    d = np.sqrt(nx * nx + ny * ny)
    a = np.clip(1.0 - d, 0, 1) ** 1.3 * 0.85
    rgb = np.dstack([np.full_like(a, 0.10), np.full_like(a, 0.09), np.full_like(a, 0.12)])
    out = np.dstack([np.round(rgb * 255), np.round(a * 255)]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def fx_target_ring(key="fx_target_ring"):
    w, h = 512, 256
    big = 2
    y, x = np.mgrid[0:h * big, 0:w * big].astype(F32) / big
    nx = (x - w / 2) / (w * 0.44)
    ny = (y - h / 2) / (h * 0.40)
    d = np.sqrt(nx * nx + ny * ny)
    ring = smoothstep(0.035, 0.0, np.abs(d - 1.0) - 0.035)
    inner = smoothstep(0.02, 0.0, np.abs(d - 0.88) - 0.012) * 0.6
    ang = np.arctan2(ny, nx)
    ticks = np.zeros_like(d)
    for k in range(4):
        a0 = k * math.pi / 2 + math.pi / 4
        da = np.abs(np.angle(np.exp(1j * (ang - a0))))
        ticks = np.maximum(ticks, (da < 0.06) * smoothstep(0.03, 0.0, np.abs(d - 1.1) - 0.08))
    glow_ = np.exp(-((d - 1.0) / 0.12) ** 2) * 0.25
    a = np.clip(ring + inner + ticks + glow_, 0, 1)
    img = Image.fromarray((a * 255).astype(np.uint8)).resize((w, h), Image.BOX)
    return to_white(np.asarray(img, F32) / 255)


def fx_rune_circle(key="fx_rune_circle"):
    n = 512
    big = 2
    N = n * big
    y, x = np.mgrid[0:N, 0:N].astype(F32) / big
    cx = cy = n / 2
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / (n / 2)
    ang = np.arctan2(y - cy, x - cx)

    def ring(r, w):
        return smoothstep(w, 0.0, np.abs(d - r) - w * 0.3)
    a = ring(0.95, 0.012) + ring(0.88, 0.006) + ring(0.62, 0.008) + ring(0.56, 0.004) + ring(0.18, 0.006)
    # hexagram between inner rings
    from PIL import ImageDraw
    img = Image.new("L", (N, N), 0)
    dr = ImageDraw.Draw(img)
    for k in range(2):
        pts = [(cx * big + math.cos(math.pi / 2 + k * math.pi + j * 2 * math.pi / 3) * 0.56 * n / 2 * big,
                cy * big - math.sin(math.pi / 2 + k * math.pi + j * 2 * math.pi / 3) * 0.56 * n / 2 * big) for j in range(3)]
        dr.line(pts + [pts[0]], fill=255, width=int(3 * big))
    # rune glyphs around the band between 0.65 and 0.86
    r = rng(key)
    for k in range(16):
        a0 = k / 16 * 2 * math.pi
        rx, ry = cx + math.cos(a0) * 0.755 * n / 2, cy + math.sin(a0) * 0.755 * n / 2
        s = 11
        kind = r.integers(4)
        segs = {0: [(-1, -1, 1, 1), (-1, 1, 1, -1)], 1: [(0, -1, 0, 1), (-1, -0.3, 1, -0.3), (-0.6, 0.6, 0.6, 0.6)],
                2: [(-1, 1, 0, -1), (0, -1, 1, 1), (-0.5, 0.2, 0.5, 0.2)], 3: [(-1, -1, 1, -1), (1, -1, -1, 1), (-1, 1, 1, 1)]}[kind]
        ca, sa = math.cos(a0 + math.pi / 2), math.sin(a0 + math.pi / 2)
        for (x0, y0, x1, y1) in segs:
            p0 = (rx + (x0 * ca - y0 * sa) * s, ry + (x0 * sa + y0 * ca) * s)
            p1 = (rx + (x1 * ca - y1 * sa) * s, ry + (x1 * sa + y1 * ca) * s)
            dr.line([(p0[0] * big, p0[1] * big), (p1[0] * big, p1[1] * big)], fill=255, width=int(3 * big))
    # small ticks on the outer band
    for k in range(48):
        a0 = k / 48 * 2 * math.pi
        p0 = (cx + math.cos(a0) * 0.88 * n / 2, cy + math.sin(a0) * 0.88 * n / 2)
        p1 = (cx + math.cos(a0) * 0.93 * n / 2, cy + math.sin(a0) * 0.93 * n / 2)
        dr.line([(p0[0] * big, p0[1] * big), (p1[0] * big, p1[1] * big)], fill=255, width=int(2 * big))
    a = np.clip(a + np.asarray(img, F32) / 255, 0, 1)
    a = np.clip(a + blur(a, 6 * big) * 0.5, 0, 1)
    img = Image.fromarray((a * 255).astype(np.uint8)).resize((n, n), Image.BOX)
    return to_white(np.asarray(img, F32) / 255)


FX = {
    # key: (fn, size, world height, pivot)
    "fx_glow": (fx_glow, (256, 256), 1.0, (0.5, 0.5)),
    "fx_spark": (fx_spark, (256, 256), 0.6, (0.5, 0.5)),
    "fx_ring": (fx_ring, (512, 512), 2.0, (0.5, 0.5)),
    "fx_bolt": (fx_bolt, (512, 256), 0.6, (0.74, 0.5)),
    "fx_arrow": (fx_arrow, (512, 128), 0.2, (0.85, 0.5)),
    "fx_slash": (fx_slash, (512, 512), 1.6, (0.5, 0.5)),
    "fx_heal": (fx_heal, (256, 512), 1.6, (0.5, 0.1)),
    "fx_smoke": (fx_smoke, (256, 256), 1.0, (0.5, 0.4)),
    "fx_leaf": (fx_leaf, (128, 128), 0.18, (0.5, 0.5)),
    "fx_firefly": (fx_firefly, (128, 128), 0.2, (0.5, 0.5)),
    "fx_shadow": (fx_shadow, (256, 128), 0.5, (0.5, 0.5)),
    "fx_target_ring": (fx_target_ring, (512, 256), 0.7, (0.5, 0.5)),
    "fx_rune_circle": (fx_rune_circle, (512, 512), 2.4, (0.5, 0.5)),
}


def _one(fn, key):
    return {key: fn(key)}


def register(reg):
    for k, (fn, size, h, piv) in FX.items():
        reg.spec(k, "Effect", size=size, height=h, pivot=piv)
        reg.job([k], _one, fn, k)
