"""Creatures, pets, demons and totems.

All unit sprites face screen-right (+x) like the heroes; the engine mirrors them with flipX.
Ghibli-like charm: readable silhouettes, soft cel shading, big expressive eyes on the friendly ones,
violet blight accents on the Hollow.
"""
import math

import numpy as np

import palette as P
from brushes import (Canvas, F32, bbox, blob, catmull, colfield, downsample, drop_shadow, ellipse, flat_fill, glow, grain,
                     outline, paint, ragged, rim_light, rng, rotate, smoothstep, stroke, to_image, warp, wash)
import characters as C
from characters import (A, Fig, GOLD, LEATHER, LEATHER_DK, Look, STEEL, STEEL_DK, blade, bow, dagger, fur, human_head, quiver,
                        stroke_w)
from environment import leaf_poly

SS = 2


def new(key, w, h):
    return Canvas(w, h, SS, seed=key)


def finish(cv, ow=2.2, rim=0.38):
    rim_light(cv, rim, 2.6)
    grain(cv, 0.018, cell=1.2, key="paper")
    outline(cv, ow)
    for (x, y, r, col, s) in getattr(cv, "post_glow", []):
        glow(cv, x, y, r, col, s, clip=False, falloff=2.2)
    rgb, a = downsample(cv.rgb, cv.a, SS)
    return to_image(rgb, a)


def post_glow(cv, x, y, r, col, s=0.5):
    if not hasattr(cv, "post_glow"):
        cv.post_glow = []
    cv.post_glow.append((x, y, r, col, s))


class Painter:
    """Small shape-painting helper for non-humanoid creatures (same look as the character rig)."""

    def __init__(self, cv, scale=1.0):
        self.cv = cv
        self.k = scale

    def shape(self, pts, col, smooth=True, shadow=0.22, n=8, **kw):
        cv = self.cv
        p = catmull(A(pts), True, n) if smooth else A(pts)
        m = cv.mask(p)
        if shadow:
            drop_shadow(cv, m, 1.6 * self.k, 3.2 * self.k, 3.0 * self.k, shadow)
        k = dict(C.CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def tube(self, pts, ws, col, shadow=0.22, cap=True, **kw):
        cv = self.cv
        m = cv.mask(stroke_w(pts, ws, cap=cap))
        if shadow:
            drop_shadow(cv, m, 1.6 * self.k, 3.2 * self.k, 3.0 * self.k, shadow)
        k = dict(C.CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def ell(self, x, y, rx, ry, col, rot=0.0, shadow=0.0, **kw):
        cv = self.cv
        m = cv.ellipse_mask(x, y, rx, ry, rot)
        if shadow:
            drop_shadow(cv, m, 1.5, 3, 3, shadow)
        k = dict(C.CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def lines(self, segs, w, col, alpha=0.8, clip=None):
        m = self.cv.lines_mask(segs, w)
        if clip is not None:
            m = m * clip
        self.cv.atop(col, m * alpha)

    def fur_strokes(self, mask, col, n=60, L=10, key="fs", direction=None, alpha=0.4, w=1.2):
        cv = self.cv
        r = rng(cv.seed, key)
        bb = bbox(mask)
        if bb is None:
            return
        ys, xs = bb
        segs = []
        tries = 0
        while len(segs) < n and tries < n * 20:
            tries += 1
            py, px = r.integers(ys.start, ys.stop), r.integers(xs.start, xs.stop)
            if mask[py, px] < 0.9:
                continue
            x, y = (px - cv.padx) / cv.s, (py - cv.pady) / cv.s
            a = direction if direction is not None else r.random() * 6.28
            a += r.normal() * 0.3
            segs.append((x, y, x + math.cos(a) * L, y + math.sin(a) * L))
        self.lines(segs, w, P.shadow_of(col, 1.1), alpha, clip=mask)

    def eye(self, x, y, rx, ry, iris, pupil=P.INK, look=(0.15, 0.0), slit=False, lid=True, shine=True):
        cv = self.cv
        m = cv.ellipse_mask(x, y, rx, ry)
        paint(cv, m, iris, line=0.9, line_w=0.9, soft=1.5, ao=0, flat=True, line_col=P.INK)
        cv.atop(P.light_of(iris, 1.4), m * smoothstep(y - ry * 0.2, y + ry, cv.yy()) * 0.6)
        px, py = x + rx * look[0], y + ry * look[1]
        if slit:
            flat_fill(cv, cv.ellipse_mask(px, py, rx * 0.18, ry * 0.85) * m, pupil, 0.95)
        else:
            flat_fill(cv, cv.ellipse_mask(px, py, rx * 0.55, ry * 0.6) * m, pupil, 0.95)
        if shine:
            flat_fill(cv, cv.ellipse_mask(px - rx * 0.3, py - ry * 0.35, rx * 0.25, ry * 0.22) * m, P.WHITE_WARM, 0.95)
            flat_fill(cv, cv.ellipse_mask(px + rx * 0.25, py + ry * 0.3, rx * 0.1, ry * 0.09) * m, P.WHITE_WARM, 0.8)
        if lid:
            lm = cv.mask(stroke_w([(x - rx * 1.1, y - ry * 0.1), (x - rx * 0.2, y - ry * 1.05), (x + rx * 0.9, y - ry * 0.6), (x + rx * 1.2, y - ry * 0.2)],
                                  [ry * 0.15, ry * 0.3, ry * 0.25, ry * 0.12]))
            flat_fill(cv, lm, P.INK, 0.95)
        return m


# ----------------------------------------------------------------------------------------
# quadrupeds (facing right)
# ----------------------------------------------------------------------------------------
def quad_body(p, x0, g, L, H, col, belly, key, fluffy=0.0, bulk=1.0, back_hump=0.0):
    """Body + legs of a four-legged animal standing on ground g, centred on x0. Returns anchors."""
    cv = p.cv
    far = P.mix(col, P.INK, 0.28)
    sh = (x0 + L * 0.3, g - H * 0.6)       # front shoulder joint
    hp = (x0 - L * 0.32, g - H * 0.62)      # hip joint
    leg_w = H * 0.2 * bulk
    belly_y = g - H * (0.4 + 0.04 * bulk)
    # far legs (behind the body)
    p.tube([(sh[0] - L * 0.1, sh[1]), (sh[0] - L * 0.1, g - H * 0.22), (sh[0] - L * 0.07, g - H * 0.04)], [leg_w * 1.1, leg_w * 0.62, leg_w * 0.6],
           far, cel=0.6)
    p.tube([(hp[0] + L * 0.08, hp[1]), (hp[0] + L * 0.14, g - H * 0.38), (hp[0] + L * 0.03, g - H * 0.2), (hp[0] + L * 0.06, g - H * 0.04)],
           [leg_w * 1.3, leg_w * 0.8, leg_w * 0.55, leg_w * 0.55], far, cel=0.6)
    for fx in (sh[0] - L * 0.06, hp[0] + L * 0.07):
        p.ell(fx, g - H * 0.04, leg_w * 0.5, leg_w * 0.3, P.mix(far, P.INK, 0.2), line=0.8)
    # torso
    pts = [(x0 - L * 0.5, g - H * 0.8), (x0 - L * 0.2, g - H * (0.88 + back_hump * 0.5)), (x0 + L * 0.15, g - H * (0.92 + back_hump)),
           (x0 + L * 0.4, g - H * 0.98), (x0 + L * 0.55, g - H * 0.8), (x0 + L * 0.55, g - H * 0.5), (x0 + L * 0.38, belly_y - H * 0.02),
           (x0, belly_y + H * 0.02), (x0 - L * 0.32, belly_y), (x0 - L * 0.55, g - H * 0.62)]
    body = p.shape(pts, col, n=6, soft=H * 0.25, shadow=0.3)
    bl = cv.mask(catmull(A([(x0 + L * 0.58, g - H * 0.75), (x0 + L * 0.5, belly_y), (x0, belly_y), (x0 - L * 0.25, belly_y - H * 0.02),
                            (x0, belly_y - H * 0.14), (x0 + L * 0.4, g - H * 0.6)]), True, 6)) * body
    cv.atop(belly, cv.blur(bl, H * 0.06) * 0.85)
    if fluffy:
        ragged(cv, body, fluffy, soft=H * 0.02, cell=H * 0.05, key=key + "rag")
    p.fur_strokes(body, col, n=int(L * 0.35), L=H * 0.08, key=key + "fs", direction=math.pi * 0.9, alpha=0.35)
    # near legs: straight front leg, hocked hind leg
    p.tube([(sh[0], sh[1] - H * 0.06), (sh[0] + L * 0.02, g - H * 0.25), (sh[0] + L * 0.04, g - H * 0.05)], [leg_w * 1.45, leg_w * 0.75, leg_w * 0.7],
           col, cel=0.6, shadow=0.3)
    p.tube([(hp[0] - L * 0.02, hp[1] - H * 0.08), (hp[0] + L * 0.06, g - H * 0.4), (hp[0] - L * 0.06, g - H * 0.2), (hp[0] - L * 0.03, g - H * 0.05)],
           [leg_w * 1.85, leg_w * 0.95, leg_w * 0.62, leg_w * 0.62], col, cel=0.6, shadow=0.3)
    for fx in (sh[0] + L * 0.06, hp[0] - L * 0.01):
        pm = p.ell(fx, g - H * 0.045, leg_w * 0.62, leg_w * 0.36, P.mix(col, belly, 0.3), line=0.9)
        p.lines([(fx - leg_w * 0.15, g - H * 0.07, fx - leg_w * 0.18, g - H * 0.01), (fx + leg_w * 0.15, g - H * 0.07, fx + leg_w * 0.18, g - H * 0.01)],
                max(0.8, leg_w * 0.05), P.INK, 0.6, clip=pm)
    return dict(body=body, neck=(x0 + L * 0.45, g - H * 0.85), shoulder=sh, hip=hp, tail=(x0 - L * 0.5, g - H * 0.75), leg_w=leg_w)


def blight_overlay(p, mask, key, strength=0.85):
    """Hollow corruption: darkening toward the underside, glowing violet vein cracks."""
    cv = p.cv
    bb = bbox(mask)
    if bb is None:
        return
    ys, xs = bb
    y0, y1 = (ys.start - cv.pady) / cv.s, (ys.stop - cv.pady) / cv.s
    grad = smoothstep(y0 + (y1 - y0) * 0.35, y1, cv.yy())
    cv.atop(P.mix(P.BLIGHT_DK, P.INK, 0.2), (mask * grad * 0.75 * strength).astype(F32))
    n = cv.noise((40, 40), key + "vn", 3)
    vein = mask * smoothstep(0.022, 0.0, np.abs(n - 0.5)) * (mask > 0.95) * smoothstep(0.45, 0.6, cv.noise(90, key + "vm", 2))
    cv.atop(P.BLIGHT_VIOLET, (vein * 0.9 * strength).astype(F32))
    cv.atop(P.BLIGHT_GLOW, (vein * 0.5 * strength).astype(F32))


def tail(p, root, pts_rel, ws, col, tip=None, fluffy=True, key="tail"):
    pts = [root] + [(root[0] + dx, root[1] + dy) for dx, dy in pts_rel]
    m = p.tube(pts, ws, col, cel=0.55)
    if fluffy:
        m2 = ragged(p.cv, m, 0.5, soft=ws[0] * 0.08, cell=ws[0] * 0.25, key=key)
    if tip:
        t = p.cv.mask(stroke_w(pts[-2:], [ws[-2] * 1.05, ws[-1] * 1.05])) * m
        p.cv.atop(tip, t * 0.9)
    return m


def canine_head(p, hx, hy, s, col, muzzle, eye_col, fierce=0.0, ear_col=None, mouth_open=0.0, key="dog"):
    """Wolf/dog head in 3/4 facing right. s = head size (skull radius)."""
    cv = p.cv
    ear_col = ear_col or col
    # far ear
    p.shape([(hx - s * 0.15, hy - s * 0.65), (hx - s * 0.05, hy - s * 1.55), (hx + s * 0.35, hy - s * 0.7)], P.mix(ear_col, P.INK, 0.2), n=4)
    # neck ruff
    ruff = p.shape([(hx - s * 1.1, hy + s * 0.2), (hx - s * 0.8, hy - s * 0.6), (hx, hy - s * 0.4), (hx + s * 0.3, hy + s * 0.6),
                    (hx - s * 0.2, hy + s * 1.2), (hx - s * 0.9, hy + s * 1.1)], col, n=5, shadow=0)
    ruff2 = ragged(cv, ruff, 0.6, soft=s * 0.04, cell=s * 0.1, key=key + "rf")
    skull = [(hx - s * 0.75, hy - s * 0.05), (hx - s * 0.55, hy - s * 0.65), (hx + s * 0.1, hy - s * 0.8), (hx + s * 0.6, hy - s * 0.45),
             (hx + s * 1.45, hy + s * 0.05), (hx + s * 1.55, hy + s * 0.35), (hx + s * 1.25, hy + s * 0.55), (hx + s * 0.55, hy + s * 0.7),
             (hx - s * 0.1, hy + s * 0.75), (hx - s * 0.6, hy + s * 0.5)]
    hm = p.shape(skull, col, n=6, soft=s * 0.35, shadow=0.25)
    mz = cv.mask(catmull(A([(hx + s * 0.4, hy + s * 0.1), (hx + s * 1.5, hy + s * 0.15), (hx + s * 1.45, hy + s * 0.55), (hx + s * 0.6, hy + s * 0.72),
                            (hx + s * 0.1, hy + s * 0.55)]), True, 5)) * hm
    cv.atop(muzzle, cv.blur(mz, s * 0.08) * 0.85)
    # cheek fur tuft
    p.shape([(hx - s * 0.5, hy + s * 0.2), (hx - s * 0.2, hy + s * 0.75), (hx - s * 0.75, hy + s * 0.65), (hx - s * 1.0, hy + s * 0.3)], muzzle, n=4,
            shadow=0.15)
    # nose
    p.ell(hx + s * 1.5, hy + s * 0.18, s * 0.17, s * 0.13, P.mix(P.INK, col, 0.15), hi=0.7, gloss=0.6)
    # mouth
    mp = [(hx + s * 1.45, hy + s * 0.42), (hx + s * 1.1, hy + s * 0.52), (hx + s * 0.75, hy + s * 0.45)]
    p.lines([(mp[0][0], mp[0][1], mp[1][0], mp[1][1]), (mp[1][0], mp[1][1], mp[2][0], mp[2][1])], s * 0.05, P.INK, 0.85)
    if fierce > 0:
        for fx in (hx + s * 1.25, hx + s * 0.95):
            p.shape([(fx - s * 0.05, hy + s * 0.48), (fx + s * 0.05, hy + s * 0.48), (fx, hy + s * 0.48 + s * 0.22 * fierce)], P.WHITE_WARM,
                    smooth=False, shadow=0, line=0.6)
    # eye
    ex, ey = hx + s * 0.55, hy - s * 0.12
    p.eye(ex, ey, s * 0.2, s * 0.15 * (1 - fierce * 0.25), eye_col, look=(0.2, 0.0), lid=True)
    if fierce > 0:
        p.tube([(ex - s * 0.3, ey - s * 0.3), (ex + s * 0.25, ey - s * 0.15)], [s * 0.1, s * 0.06], P.mix(col, P.INK, 0.4), shadow=0)
    # near ear
    ear = p.shape([(hx - s * 0.55, hy - s * 0.5), (hx - s * 0.4, hy - s * 1.5), (hx + s * 0.05, hy - s * 0.65)], ear_col, n=4, shadow=0.2)
    inner = cv.mask(catmull(A([(hx - s * 0.45, hy - s * 0.6), (hx - s * 0.38, hy - s * 1.25), (hx - s * 0.12, hy - s * 0.7)]), True, 4)) * ear
    cv.atop(P.mix(muzzle, P.ROSE, 0.3), inner * 0.8)
    return hm


def quad_animal(key, kind):
    cv = new(key, 1024, 512)
    p = Painter(cv)
    r = rng(key)
    g = 488.0
    if kind in ("wolf", "wolf_blighted", "pet_wolf"):
        col = {"wolf": P.hx("8c929e"), "wolf_blighted": P.hx("8a8496"), "pet_wolf": P.hx("9a8a7a")}[kind]
        belly = {"wolf": P.hx("e8e2d6"), "wolf_blighted": P.hx("cfc8d6"), "pet_wolf": P.hx("f2e8d8")}[kind]
        eye = {"wolf": P.hx("e8b04f"), "wolf_blighted": P.BLIGHT_GLOW, "pet_wolf": P.hx("8fb04f")}[kind]
        L, H = 480, 330
        x0 = 470
        tail(p, (x0 - L * 0.48, g - H * 0.78), [(-L * 0.2, H * 0.1), (-L * 0.32, H * 0.42), (-L * 0.3, H * 0.6)], [H * 0.16, H * 0.22, H * 0.18, H * 0.08],
             col, tip=belly, key="tl")
        an = quad_body(p, x0, g, L, H, col, belly, key, fluffy=0.4)
        # dark saddle along the back
        sad = cv.mask(catmull(A([(x0 - L * 0.48, g - H * 0.8), (x0 + L * 0.2, g - H * 0.95), (x0 + L * 0.45, g - H * 0.92), (x0 + L * 0.3, g - H * 0.75),
                                 (x0 - L * 0.4, g - H * 0.68)]), True, 6)) * an["body"]
        cv.atop(P.mix(col, P.INK, 0.35), cv.blur(sad, 8) * 0.6)
        head_s = H * 0.3
        hx, hy = x0 + L * 0.55, g - H * 1.02
        canine_head(p, hx, hy, head_s, col, belly, eye, fierce={"wolf": 0.6, "wolf_blighted": 1.0, "pet_wolf": 0.0}[kind], key=key)
        if kind == "wolf_blighted":
            blight_overlay(p, (cv.a > 0.5).astype(F32), key)
            from props import crystal_cluster
            for (cx_, cy_, s_) in ((x0 - L * 0.1, g - H * 0.86, 70), (x0 + L * 0.2, g - H * 0.92, 56), (x0 - L * 0.35, g - H * 0.8, 50)):
                crystal_cluster(cv, cx_, cy_, s_, r, key + "c%d" % int(cx_), n=3, glow_amt=0.8)
            post_glow(cv, hx + head_s * 0.55, hy - head_s * 0.12, head_s * 0.9, P.BLIGHT_GLOW, 0.6)
        if kind == "pet_wolf":
            # red scarf collar
            nx, ny = hx - head_s * 0.6, hy + head_s * 0.9
            p.tube([(nx - head_s * 0.4, ny - head_s * 0.5), (nx + head_s * 0.1, ny + head_s * 0.1), (nx + head_s * 0.6, ny - head_s * 0.2)],
                   [head_s * 0.25] * 3, P.hx("d9483b"), cap=False)
            p.shape([(nx + head_s * 0.05, ny), (nx + head_s * 0.45, ny + head_s * 0.15), (nx + head_s * 0.15, ny + head_s * 0.7)], P.hx("d9483b"), n=3)
            p.ell(nx + head_s * 0.1, ny + head_s * 0.12, head_s * 0.1, head_s * 0.1, P.HONEY, hi=0.7, gloss=0.6)
    elif kind in ("boar", "pet_boar"):
        col = P.hx("7a5a46") if kind == "boar" else P.hx("9a7458")
        belly = P.hx("b89a7e") if kind == "boar" else P.hx("d8bfa0")
        L, H = 470, 300
        x0 = 480
        an = quad_body(p, x0, g, L, H, col, belly, key, fluffy=0.5, bulk=1.45, back_hump=0.14)
        # bristly mane along the back
        polys = []
        for k in range(22):
            t = k / 21
            bx = x0 - L * 0.35 + t * L * 0.75 + r.normal() * 4
            by = g - H * (0.88 + 0.12 * math.sin(t * math.pi) + 0.06 * t)
            polys.append(stroke_w([(bx, by + H * 0.08), (bx - H * 0.06, by - H * 0.05 - r.random() * H * 0.08)], [H * 0.06, 1.0]))
        mm = cv.polys_mask(polys)
        paint(cv, mm, P.mix(col, P.INK, 0.4), line=0.8, line_w=1.0, cel=0.6, soft=3)
        hx, hy = x0 + L * 0.52, g - H * 0.72
        s = H * 0.34
        far_ear = p.shape([(hx - s * 0.3, hy - s * 0.6), (hx - s * 0.25, hy - s * 1.25), (hx + s * 0.15, hy - s * 0.65)], P.mix(col, P.INK, 0.2), n=4)
        head = [(hx - s * 0.6, hy - s * 0.6), (hx + s * 0.2, hy - s * 0.75), (hx + s * 1.1, hy - s * 0.15), (hx + s * 1.35, hy + s * 0.1),
                (hx + s * 1.3, hy + s * 0.55), (hx + s * 0.6, hy + s * 0.8), (hx - s * 0.4, hy + s * 0.75), (hx - s * 0.8, hy + s * 0.2)]
        hm = p.shape(head, col, n=6, soft=s * 0.3, shadow=0.3)
        p.fur_strokes(hm, col, n=40, L=s * 0.15, key="hb", direction=math.pi * 0.95)
        sn = p.ell(hx + s * 1.3, hy + s * 0.32, s * 0.22, s * 0.3, P.mix(belly, P.ROSE, 0.4), hi=0.5)
        p.ell(hx + s * 1.33, hy + s * 0.25, s * 0.05, s * 0.08, P.INK, line=0, shadow=0)
        p.ell(hx + s * 1.33, hy + s * 0.42, s * 0.05, s * 0.08, P.INK, line=0, shadow=0)
        tusk = 1.0 if kind == "boar" else 0.6
        p.shape([(hx + s * 0.95, hy + s * 0.55), (hx + s * 1.05, hy + s * 0.5), (hx + s * 1.15, hy + s * (0.1 + 0.2 * (1 - tusk))),
                 (hx + s * 1.0, hy + s * 0.25)], P.hx("f4ecd8"), n=4, shadow=0.15)
        p.eye(hx + s * 0.35, hy - s * 0.12, s * 0.13 * (1.4 if kind == "pet_boar" else 1), s * 0.11 * (1.4 if kind == "pet_boar" else 1),
              P.hx("3a2a22") if kind == "boar" else P.hx("6a4a32"), lid=True)
        ear = p.shape([(hx - s * 0.5, hy - s * 0.5), (hx - s * 0.65, hy - s * 1.2), (hx - s * 0.05, hy - s * 0.65)], col, n=4)
        p.tube([(x0 - L * 0.5, g - H * 0.75), (x0 - L * 0.58, g - H * 0.62), (x0 - L * 0.56, g - H * 0.5)], [H * 0.05, H * 0.04, H * 0.02], col)
        if kind == "pet_boar":
            bl = [(x0 - L * 0.15, g - H * 0.92), (x0 + L * 0.2, g - H * 0.95), (x0 + L * 0.25, g - H * 0.55), (x0 - L * 0.18, g - H * 0.55)]
            p.shape(bl, P.hx("3f8f86"), n=4)
            p.lines([(x0 - L * 0.17, g - H * 0.6, x0 + L * 0.24, g - H * 0.6)], 6, P.HONEY, 0.9)
    elif kind == "pet_bear":
        col, belly = P.hx("7a5638"), P.hx("b88a5e")
        L, H = 520, 360
        x0 = 470
        an = quad_body(p, x0, g, L, H, col, belly, key, fluffy=0.6, bulk=1.9, back_hump=0.2)
        hx, hy = x0 + L * 0.5, g - H * 0.85
        s = H * 0.3
        for ex_, ey_ in ((hx - s * 0.45, hy - s * 0.75), (hx + s * 0.35, hy - s * 0.85)):
            p.ell(ex_, ey_, s * 0.28, s * 0.26, col, shadow=0.2)
            p.ell(ex_, ey_, s * 0.15, s * 0.13, P.mix(belly, P.ROSE, 0.3), line=0)
        hm = p.shape(ellipse(hx, hy, s * 0.95, s * 0.8, 0, 32), col, smooth=False, soft=s * 0.4, shadow=0.3)
        p.fur_strokes(hm, col, n=40, L=s * 0.15, key="bh")
        p.shape(ellipse(hx + s * 0.55, hy + s * 0.25, s * 0.5, s * 0.35, 0, 24), belly, smooth=False, shadow=0)
        p.ell(hx + s * 0.92, hy + s * 0.12, s * 0.16, s * 0.12, P.INK, hi=0.7, gloss=0.5)
        p.lines([(hx + s * 0.9, hy + s * 0.25, hx + s * 0.85, hy + s * 0.4), (hx + s * 0.85, hy + s * 0.4, hx + s * 0.65, hy + s * 0.45)], s * 0.05,
                P.INK, 0.8)
        p.eye(hx + s * 0.1, hy - s * 0.15, s * 0.12, s * 0.12, P.hx("3a2618"), lid=False)
        p.eye(hx + s * 0.55, hy - s * 0.2, s * 0.1, s * 0.11, P.hx("3a2618"), lid=False)
        p.tube([(x0 + L * 0.3, g - H * 0.75), (x0 + L * 0.52, g - H * 0.62)], [H * 0.12, H * 0.12], P.hx("3f8f86"), cap=False)
    elif kind == "pet_cat":
        col, belly = P.hx("d8954a"), P.hx("f6e6cc")
        L, H = 420, 280
        x0 = 470
        tail(p, (x0 - L * 0.48, g - H * 0.75), [(-L * 0.2, -H * 0.1), (-L * 0.3, -H * 0.5), (-L * 0.22, -H * 0.8)], [H * 0.12, H * 0.12, H * 0.1, H * 0.09],
             col, tip=P.mix(col, P.INK, 0.4), key="ctl")
        an = quad_body(p, x0, g, L, H, col, belly, key, fluffy=0.25, bulk=0.9)
        # tabby stripes
        stripes = []
        for k in range(6):
            sx = x0 - L * 0.35 + k * L * 0.13
            stripes.append(stroke_w([(sx, g - H * 0.92), (sx + L * 0.03, g - H * 0.75), (sx - L * 0.01, g - H * 0.6)], [H * 0.06, H * 0.04, 1]))
        sm = cv.polys_mask(stripes) * an["body"]
        cv.atop(P.mix(col, P.TERRACOTTA_DK, 0.6), sm * 0.8)
        hx, hy = x0 + L * 0.52, g - H * 1.05
        s = H * 0.32
        for (ex_, sgn) in ((hx - s * 0.5, -1), (hx + s * 0.45, 1)):
            e = p.shape([(ex_ - s * 0.35, hy - s * 0.55), (ex_ + sgn * s * 0.05, hy - s * 1.45), (ex_ + s * 0.35, hy - s * 0.6)], col, n=4)
            p.lines([(ex_ + sgn * s * 0.05, hy - s * 1.45, ex_ + sgn * s * 0.05, hy - s * 1.7)], s * 0.05, P.INK, 0.9)
        hm = p.shape(ellipse(hx, hy, s * 1.0, s * 0.82, 0, 32), col, smooth=False, soft=s * 0.4, shadow=0.3)
        p.shape(ellipse(hx + s * 0.25, hy + s * 0.38, s * 0.55, s * 0.38, 0, 24), belly, smooth=False, shadow=0)
        p.shape([(hx + s * 0.18, hy + s * 0.12), (hx + s * 0.38, hy + s * 0.12), (hx + s * 0.28, hy + s * 0.24)], P.ROSE, smooth=False, shadow=0)
        p.lines([(hx + s * 0.28, hy + s * 0.25, hx + s * 0.28, hy + s * 0.38), (hx + s * 0.28, hy + s * 0.38, hx + s * 0.12, hy + s * 0.46),
                 (hx + s * 0.28, hy + s * 0.38, hx + s * 0.44, hy + s * 0.46)], s * 0.04, P.INK, 0.8)
        for k in range(3):
            p.lines([(hx + s * 0.65, hy + s * (0.3 + k * 0.08), hx + s * 1.25, hy + s * (0.2 + k * 0.14))], s * 0.02, P.WHITE_WARM, 0.8)
        p.eye(hx - s * 0.2, hy - s * 0.1, s * 0.2, s * 0.22, P.hx("8fc04f"), slit=True, lid=True)
        p.eye(hx + s * 0.5, hy - s * 0.12, s * 0.17, s * 0.21, P.hx("8fc04f"), slit=True, lid=True)
        p.tube([(hx - s * 0.6, hy + s * 0.75), (hx, hy + s * 0.92), (hx + s * 0.55, hy + s * 0.75)], [s * 0.14] * 3, P.hx("3f78b5"), cap=False)
        p.ell(hx, hy + s * 0.98, s * 0.11, s * 0.11, P.HONEY, hi=0.7, gloss=0.6)
    elif kind == "felhunter":
        col, belly = P.hx("6a4a6e"), P.hx("8fbf6a")
        L, H = 560, 320
        x0 = 480
        tail(p, (x0 - L * 0.48, g - H * 0.8), [(-L * 0.2, -H * 0.05), (-L * 0.35, H * 0.2)], [H * 0.12, H * 0.07, H * 0.02], col, fluffy=False, key="ft")
        an = quad_body(p, x0, g, L, H, col, P.mix(col, P.LAVENDER, 0.4), key, bulk=1.1, back_hump=0.1)
        # spines
        polys = []
        for k in range(8):
            t = k / 7
            bx = x0 - L * 0.35 + t * L * 0.7
            by = g - H * (0.86 + 0.08 * math.sin(t * math.pi))
            polys.append(A([(bx - H * 0.06, by + H * 0.04), (bx + H * 0.05, by - H * 0.22), (bx + H * 0.08, by + H * 0.04)]))
        sm = cv.polys_mask(polys)
        paint(cv, sm, P.hx("d8d0b8"), line=0.9, line_w=1.0, cel=0.7, soft=2)
        # glowing fel runes along the flank
        rm = cv.mask(catmull(A([(x0 - L * 0.3, g - H * 0.75), (x0 + L * 0.25, g - H * 0.8), (x0 + L * 0.25, g - H * 0.6), (x0 - L * 0.3, g - H * 0.58)]), True, 4))
        C.runes(Fig(cv, Look()), (rm * (cv.a > 0.99)).astype(F32), belly, n=4, s=0.25, key="fel")
        hx, hy = x0 + L * 0.56, g - H * 0.95
        s = H * 0.3
        # tentacles from the head
        for k in range(2):
            a0 = -math.pi * (0.65 + 0.12 * k)
            pts = [(hx - s * 0.2, hy - s * 0.4)]
            for j in range(1, 5):
                pts.append((pts[-1][0] - s * 0.35 - j * s * 0.05, pts[-1][1] + (j - 1.5) * s * 0.25 * (1 if k else -0.6)))
            p.tube(pts, [s * 0.22, s * 0.18, s * 0.14, s * 0.1, s * 0.05], P.mix(col, P.VIOLET, 0.3), cel=0.6)
            p.ell(pts[-1][0], pts[-1][1], s * 0.08, s * 0.08, belly, hi=0.7)
        head = [(hx - s * 0.6, hy - s * 0.5), (hx + s * 0.3, hy - s * 0.7), (hx + s * 1.3, hy - s * 0.1), (hx + s * 1.35, hy + s * 0.4),
                (hx + s * 0.5, hy + s * 0.75), (hx - s * 0.5, hy + s * 0.6)]
        p.shape(head, col, n=5, soft=s * 0.3)
        p.lines([(hx + s * 1.3, hy + s * 0.35, hx + s * 0.4, hy + s * 0.45)], s * 0.08, P.INK, 0.9)
        for k in range(5):
            fx = hx + s * (0.55 + k * 0.17)
            p.shape([(fx, hy + s * 0.36), (fx + s * 0.08, hy + s * 0.36), (fx + s * 0.04, hy + s * 0.52)], P.WHITE_WARM, smooth=False, shadow=0,
                    line=0.6)
        p.eye(hx + s * 0.45, hy - s * 0.15, s * 0.16, s * 0.1, belly, slit=True)
        post_glow(cv, hx + s * 0.45, hy - s * 0.15, s * 0.8, belly, 0.6)
    return finish(cv)


# ----------------------------------------------------------------------------------------
# spider
# ----------------------------------------------------------------------------------------
def cr_spider(key="cr_spider"):
    cv = new(key, 1024, 512)
    p = Painter(cv)
    r = rng(key)
    g = 490
    col = P.hx("5a4a3a")
    x0 = 470
    legs = []
    for side in (0, 1):     # 0 far, 1 near
        for k in range(4):
            bx = x0 + 60 - k * 40
            by = g - 190
            dirx = 1 if k < 2 else -1
            kx = bx + dirx * (120 + k * 10) + (k - 1.5) * 40
            ky = by - 120 + k * 10
            fx = bx + dirx * (200 + k * 20) + (k - 1.5) * 90
            legs.append((side, [(bx, by), (kx, ky), (fx, g - 4)]))
    for side, pts in legs:
        c = col if side else P.mix(col, P.INK, 0.35)
        w = 26 if side else 20
        p.tube(pts, [w, w * 0.8, w * 0.35], c, cel=0.65)
        if side:
            for j in range(3):
                x = pts[1][0] + (pts[2][0] - pts[1][0]) * (0.2 + j * 0.25)
                y = pts[1][1] + (pts[2][1] - pts[1][1]) * (0.2 + j * 0.25)
                p.ell(x, y, 10, 6, P.hx("d8b86a"), line=0.6, shadow=0)
    # abdomen with pattern
    ab = p.shape(ellipse(x0 - 150, g - 230, 190, 150, -0.2, 40), col, smooth=False, soft=60, shadow=0.3, hi=0.4)
    pat = cv.mask(catmull(A([(x0 - 250, g - 300), (x0 - 150, g - 340), (x0 - 50, g - 300), (x0 - 80, g - 230), (x0 - 150, g - 200), (x0 - 220, g - 230)]),
                          True, 5)) * ab
    cv.atop(P.hx("d8b86a"), pat * 0.85)
    for k in range(3):
        cv.atop(P.mix(col, P.INK, 0.3), cv.ellipse_mask(x0 - 150 - k * 10, g - 290 + k * 30, 30 - k * 6, 10) * ab * 0.8)
    mo = ab * smoothstep(0.6, 0.68, cv.noise(30, "mo", 3)) * smoothstep(g - 250, g - 360, cv.yy())
    paint(cv, ragged(cv, mo.astype(F32), 0.4, 1.5, 5, "mr"), P.MOSS, line=0.5, soft=4, hi=0.5, ao=0)
    p.fur_strokes(ab, col, n=80, L=12, key="sf", alpha=0.3)
    # cephalothorax + head
    p.shape(ellipse(x0 + 70, g - 200, 110, 85, 0.1, 36), col, smooth=False, soft=40, shadow=0.3, hi=0.4)
    hx, hy = x0 + 160, g - 190
    p.shape(ellipse(hx, hy, 70, 62, 0, 32), P.mix(col, P.TERRACOTTA, 0.15), smooth=False, soft=30)
    for (ex, ey, rr) in ((hx + 20, hy - 20, 16), (hx + 50, hy - 18, 13), (hx + 10, hy + 8, 10), (hx + 40, hy + 10, 9), (hx + 62, hy, 7), (hx - 10, hy - 30, 8)):
        p.eye(ex, ey, rr, rr, P.hx("c8402e"), lid=False)
    for s in (-1, 1):
        p.tube([(hx + 40, hy + 30), (hx + 60, hy + 70 + s * 6), (hx + 45, hy + 95)], [18, 14, 6], P.mix(col, P.INK, 0.3))
    return finish(cv)


# ----------------------------------------------------------------------------------------
# mosslings
# ----------------------------------------------------------------------------------------
def mossling(key, shaman=False):
    cv = new(key, 512, 512)
    p = Painter(cv)
    r = rng(key)
    g = 490
    cx = 250
    moss, moss_dk, moss_lt = P.hx("7fa456"), P.hx("4f7a3a"), P.hx("c4dc8e")
    if shaman:
        st = [(cx + 140, g - 4), (cx + 135, g - 200), (cx + 150, g - 330)]
        p.tube(st, [12, 12, 10], P.WOOD)
        for k in range(3):
            a = -1.2 + k * 0.6
            p.tube([(cx + 150, g - 330), (cx + 150 + math.cos(a) * 50, g - 330 + math.sin(a) * 50)], [8, 3], P.WOOD)
        p.shape(leaf_poly(cx + 150, g - 335, 60, -1.9, 0.5), moss_lt, smooth=False)
        p.ell(cx + 145, g - 290, 14, 18, P.hx("8fe3d6"), hi=0.8, gloss=0.6)
        post_glow(cv, cx + 145, g - 290, 60, P.hx("8fe3d6"), 0.5)
    # feet
    for s in (-1, 1):
        p.ell(cx + s * 55, g - 22, 46, 26, moss_dk, shadow=0.2)
    body = []
    for k in range(14):
        a = k / 14 * 2 * math.pi
        rr = 1.0 + 0.08 * math.sin(a * 5 + 1)
        body.append((cx + math.cos(a) * 150 * rr, g - 150 + math.sin(a) * 140 * rr))
    bm = p.shape(body, moss, n=5, soft=60, shadow=0.3, hi=0.45, light_col=moss_lt)
    bm2 = ragged(cv, bm, 0.6, soft=2, cell=7, key="br")
    # moss tufts texture
    tufts = []
    for k in range(40):
        a = r.random() * 2 * math.pi
        d = math.sqrt(r.random()) * 0.85
        x, y = cx + math.cos(a) * 140 * d, g - 150 + math.sin(a) * 130 * d
        tufts.append(blob(x, y, 14 + r.random() * 10, 10 + r.random() * 6, r, 0.25, 6))
    tm = cv.polys_mask(tufts) * bm
    paint(cv, tm.astype(F32), P.mix(moss, moss_lt, 0.4), line=0.4, line_w=0.8, soft=4, ao=0.3, hi=0.5, var=0.06)
    # little arms
    for s in (-1, 1):
        p.tube([(cx + s * 130, g - 150), (cx + s * 175, g - 105), (cx + s * 180, g - 75)], [30, 24, 20], moss_dk)
    # face: big eyes, mischievous grin
    for (ex, sz) in ((cx - 45, 1.0), (cx + 55, 0.92)):
        p.ell(ex, g - 175, 34 * sz, 40 * sz, P.WHITE_WARM, line=0.9, soft=4, ao=0, hi=0.3, shadow=0.15)
        flat_fill(cv, cv.ellipse_mask(ex + 8, g - 170, 22 * sz, 28 * sz), P.hx("2a2620"), 0.95)
        flat_fill(cv, cv.ellipse_mask(ex + 1, g - 182, 9 * sz, 10 * sz), P.WHITE_WARM, 0.95)
        flat_fill(cv, cv.ellipse_mask(ex + 14, g - 160, 4 * sz, 4 * sz), P.WHITE_WARM, 0.85)
    mouth = cv.mask(catmull(A([(cx - 30, g - 115), (cx + 40, g - 118), (cx + 10, g - 92)]), True, 5))
    flat_fill(cv, mouth, P.hx("4a2a22"), 0.95)
    flat_fill(cv, cv.mask(A([(cx - 18, g - 116), (cx - 6, g - 116), (cx - 12, g - 104)])), P.WHITE_WARM, 0.95)
    for bx in (cx - 80, cx + 95):
        flat_fill(cv, cv.blur(cv.ellipse_mask(bx, g - 128, 20, 10), 4), P.BLUSH, 0.45)
    # leaf hat
    hat = leaf_poly(cx - 120, g - 285, 260, -0.25, 0.48)
    hm = p.shape(hat, P.hx("5f9a3a"), smooth=False, shadow=0.35, hi=0.45, light_col=moss_lt)
    p.lines([(cx - 115, g - 285, cx + 130, g - 345)], 4, P.mix(P.hx("5f9a3a"), P.INK, 0.3), 0.7, clip=hm)
    p.lines([(cx - 115 + k * 45, g - 285 - k * 11, cx - 95 + k * 45, g - 315 - k * 11) for k in range(1, 5)], 2.5, P.mix(P.hx("5f9a3a"), P.INK, 0.3),
            0.6, clip=hm)
    p.tube([(cx + 130, g - 345), (cx + 155, g - 380), (cx + 150, g - 400)], [8, 6, 3], P.WOOD)
    if shaman:
        for k in range(9):
            t = k / 8
            p.ell(cx - 90 + t * 180, g - 95 + math.sin(t * math.pi) * 40, 11, 11, [P.HONEY, P.hx("8fe3d6"), P.TERRACOTTA][k % 3], hi=0.7,
                  gloss=0.5, shadow=0.15)
        p.shape([(cx - 60, g - 290), (cx - 90, g - 380), (cx - 70, g - 390), (cx - 45, g - 300)], P.WHITE_WARM, n=4)
        p.shape([(cx - 40, g - 292), (cx - 50, g - 375), (cx - 32, g - 380), (cx - 25, g - 298)], P.VERMILION, n=4)
    return finish(cv)


# ----------------------------------------------------------------------------------------
# hollow creatures
# ----------------------------------------------------------------------------------------
def cr_hollow_wisp(key="cr_hollow_wisp"):
    cv = new(key, 512, 512)
    p = Painter(cv)
    cx, cy = 270, 300
    # will-o'-wisp: round glowing body with a flame tail licking up and back (left)
    pts = [(cx - 95, cy + 20), (cx - 80, cy - 60), (cx - 120, cy - 150), (cx - 170, cy - 230), (cx - 70, cy - 170), (cx - 10, cy - 120),
           (cx + 40, cy - 150), (cx + 30, cy - 95), (cx + 95, cy - 40), (cx + 110, cy + 40), (cx + 70, cy + 110), (cx, cy + 135), (cx - 70, cy + 105)]
    m = p.shape(pts, P.mix(P.BLIGHT, P.LAVENDER, 0.45), n=6, soft=60, cel=0.3, hi=0.55, light_col=P.BLIGHT_GLOW, line=0.6)
    inner = cv.blur(cv.ellipse_mask(cx + 5, cy + 20, 60, 70), 25) * m
    cv.atop(P.mix(P.BLIGHT_GLOW, P.WHITE_WARM, 0.5), inner * 0.85)
    for (ex, ey) in ((cx - 25, cy + 15), (cx + 38, cy + 12)):
        flat_fill(cv, cv.ellipse_mask(ex, ey, 15, 23), P.hx("2a2236"), 0.95)
        flat_fill(cv, cv.ellipse_mask(ex + 3, ey - 5, 5, 7), P.BLIGHT_GLOW, 0.95)
    r = rng(key)
    for i in range(5):
        a = r.random() * 6.28
        post_glow(cv, cx + math.cos(a) * 170, cy + math.sin(a) * 150, 9, P.BLIGHT_GLOW, 0.9)
    post_glow(cv, cx, cy, 230, P.BLIGHT_GLOW, 0.35)
    return finish(cv, ow=1.8)


def cr_hollow_treant(key="cr_hollow_treant"):
    cv = new(key, 512, 1024)
    p = Painter(cv)
    r = rng(key)
    g = 990
    cx = 256
    bark = P.mix(P.BLIGHT, P.WOOD, 0.35)
    # root legs
    for s in (-1, 1):
        p.tube([(cx + s * 50, g - 330), (cx + s * 90, g - 150), (cx + s * 120, g - 10)], [80, 60, 40], P.mix(bark, P.INK, 0.15), tex=cv.noise((30, 4), "bk", 3))
        for k in range(3):
            p.tube([(cx + s * 120, g - 30), (cx + s * (150 + k * 30), g - 10 + k * 3)], [26 - k * 5, 6], P.mix(bark, P.INK, 0.15))
    body = [(cx - 110, g - 300), (cx - 120, g - 600), (cx - 90, g - 760), (cx - 30, g - 820), (cx + 50, g - 810), (cx + 110, g - 740), (cx + 120, g - 580),
            (cx + 110, g - 300), (cx, g - 270)]
    bm = p.shape(body, bark, n=6, soft=60, tex=cv.noise((60, 5), "bk2", 3), hi=0.35)
    segs = [(cx + (r.random() - 0.5) * 200, g - 300 - r.random() * 500, 0, 0) for _ in range(30)]
    segs = [(x, y, x + r.normal() * 4, y + 50 + r.random() * 80) for x, y, _, _ in segs]
    p.lines(segs, 2.5, P.shadow_of(bark, 1.3), 0.5, clip=bm)
    # branch arms
    for s in (-1, 1):
        pts = [(cx + s * 100, g - 640), (cx + s * 190, g - 560), (cx + s * 230, g - 430), (cx + s * 210, g - 360)]
        p.tube(pts, [60, 40, 28, 14], bark)
        for k in range(3):
            a = math.pi / 2 + s * (0.3 - k * 0.35)
            p.tube([(cx + s * 210, g - 380), (cx + s * 210 + math.cos(a) * 60, g - 380 + math.sin(a) * 60)], [12, 3], bark)
    # crown of dead branches
    for k in range(7):
        a = -math.pi / 2 + (k - 3) * 0.32
        pts = [(cx + (k - 3) * 15, g - 780), (cx + (k - 3) * 15 + math.cos(a) * 110, g - 780 + math.sin(a) * 110),
               (cx + (k - 3) * 15 + math.cos(a + 0.2) * 180, g - 780 + math.sin(a + 0.2) * 180)]
        p.tube(pts, [30, 16, 4], bark)
    for k in range(6):
        x, y = cx + (r.random() - 0.5) * 300, g - 850 - r.random() * 120
        p.shape(leaf_poly(x, y, 30, r.random() * 6, 0.5), P.mix(P.LEAF_YELLOW, P.BLIGHT, 0.6), smooth=False, shadow=0.1)
    # face: hollow glowing eyes and mouth
    for (ex, ey, rx, ry) in ((cx - 45, g - 640, 26, 32), (cx + 45, g - 645, 24, 30)):
        flat_fill(cv, cv.ellipse_mask(ex, ey, rx, ry), P.hx("1e1a26"), 0.95)
        flat_fill(cv, cv.ellipse_mask(ex + 4, ey + 2, rx * 0.4, ry * 0.4), P.BLIGHT_GLOW, 0.95)
        post_glow(cv, ex, ey, 70, P.BLIGHT_GLOW, 0.5)
    mm = cv.mask(catmull(A([(cx - 50, g - 540), (cx + 50, g - 545), (cx + 30, g - 500), (cx - 30, g - 498)]), True, 5))
    flat_fill(cv, mm, P.hx("1e1a26"), 0.95)
    blight_overlay(p, (cv.a > 0.95).astype(F32), key, 0.8)
    from props import crystal_cluster
    crystal_cluster(cv, cx - 70, g - 760, 90, r, key + "c1", n=4)
    crystal_cluster(cv, cx + 80, g - 330, 70, r, key + "c2", n=3)
    return finish(cv)


def cr_hollow_warden(key="cr_hollow_warden"):
    """Boss: an enormous corrupted stag spirit with lantern-hung antlers."""
    cv = new(key, 1024, 1024)
    p = Painter(cv, scale=1.5)
    r = rng(key)
    g = 1000
    coat = P.hx("e9e6f2")
    deep = P.hx("4e4466")
    x0 = 390
    L, H = 640, 540
    hx, hy = x0 + L * 0.62, g - H * 1.36
    # ---------------- antlers (behind everything): wide crown with lanterns
    lanterns = []
    antler = P.hx("b9ad9c")

    def branch(x, y, ang, length, w, depth, side):
        ex, ey = x + math.cos(ang) * length, y + math.sin(ang) * length
        mx, my = x + math.cos(ang + side * 0.15) * length * 0.55, y + math.sin(ang + side * 0.15) * length * 0.55
        p.tube([(x, y), (mx, my), (ex, ey)], [w, w * 0.75, max(3, w * 0.35)], antler, cel=0.6, hi=0.4, shadow=0.15)
        if depth == 0:
            lanterns.append((ex, ey))
            return
        branch(ex, ey, ang - side * 0.35 + r.normal() * 0.08, length * 0.72, w * 0.68, depth - 1, side)
        tx, ty = x + math.cos(ang) * length * 0.55, y + math.sin(ang) * length * 0.55
        branch(tx, ty, ang + side * 0.6, length * 0.55, w * 0.55, depth - 1, side)
    for side, base in ((-1, (hx - 40, hy - 50)), (1, (hx + 10, hy - 55))):
        branch(base[0], base[1], -math.pi / 2 + side * 0.5, 175, 34, 2, side)
    # ---------------- far legs
    for lx, ox in ((x0 + L * 0.22, -10), (x0 - L * 0.22, 20)):
        p.tube([(lx, g - H * 0.6), (lx + ox, g - H * 0.32), (lx + 8, g - 22)], [70, 34, 28], P.mix(deep, P.INK, 0.25), cel=0.6)
        p.ell(lx + 10, g - 16, 26, 16, P.INK_SOFT)
    # ---------------- tattered spirit cloth over the back
    cloth = [(x0 + L * 0.3, g - H * 1.0), (x0 - L * 0.1, g - H * 0.95), (x0 - L * 0.5, g - H * 0.8), (x0 - L * 0.62, g - H * 0.45),
             (x0 - L * 0.52, g - H * 0.48), (x0 - L * 0.5, g - H * 0.3), (x0 - L * 0.4, g - H * 0.5), (x0 - L * 0.32, g - H * 0.38),
             (x0 - L * 0.2, g - H * 0.62), (x0 + L * 0.25, g - H * 0.72)]
    cmk = p.shape(cloth, P.hx("5a4a78"), n=4, soft=40, hi=0.35)
    C.runes(Fig(cv, Look()), (cmk * (cv.a > 0.95)).astype(F32), P.BLIGHT_GLOW, n=6, s=0.3, key="wr")
    # ---------------- body
    body = [(x0 - L * 0.52, g - H * 0.82), (x0 - L * 0.2, g - H * 0.9), (x0 + L * 0.2, g - H * 0.95), (x0 + L * 0.42, g - H * 1.02),
            (x0 + L * 0.56, g - H * 0.82), (x0 + L * 0.54, g - H * 0.55), (x0 + L * 0.34, g - H * 0.46), (x0 - L * 0.1, g - H * 0.5),
            (x0 - L * 0.44, g - H * 0.5), (x0 - L * 0.6, g - H * 0.64)]
    bm = p.shape(body, coat, n=6, soft=100, shadow=0.35, hi=0.45, light_col=P.WHITE_WARM)
    p.fur_strokes(bm, coat, n=140, L=24, key="wf", direction=math.pi * 0.95, alpha=0.25, w=2.2)
    # cloth drape over the top of the back
    p.shape([(x0 + L * 0.3, g - H * 1.0), (x0 - L * 0.35, g - H * 0.92), (x0 - L * 0.42, g - H * 0.72), (x0 - L * 0.1, g - H * 0.68),
             (x0 + L * 0.28, g - H * 0.78)], P.hx("6a5a8a"), n=4, hi=0.4)
    p.tube([(x0 - L * 0.4, g - H * 0.75), (x0 - L * 0.05, g - H * 0.7), (x0 + L * 0.3, g - H * 0.8)], [16, 16, 16], P.hx("e2c98a"), cap=False)
    # ---------------- near legs
    for lx, ox in ((x0 + L * 0.34, 10), (x0 - L * 0.36, -24)):
        p.tube([(lx, g - H * 0.66), (lx + ox, g - H * 0.33), (lx + 10, g - 22)], [96, 42, 34], coat, cel=0.6, hi=0.4)
        p.ell(lx + 14, g - 16, 30, 18, P.INK_SOFT, hi=0.5)
        fet = cv.mask(stroke_w([(lx + ox * 0.5, g - H * 0.28), (lx + 10, g - 30)], [46, 40])) * (cv.a > 0.5)
        cv.atop(P.WHITE_WARM, fet.astype(F32) * 0.0)
    blight_overlay(p, (cv.a > 0.95).astype(F32) * smoothstep(g - H * 1.1, g - H * 0.2, cv.yy()), key, 0.95)
    # ---------------- neck with a shaggy white mane, rope collar
    neck = [(x0 + L * 0.28, g - H * 0.95), (hx - 70, hy + 10), (hx + 30, hy + 30), (x0 + L * 0.6, g - H * 0.62)]
    p.shape(neck, coat, n=5, soft=60, hi=0.45)
    mane = []
    for k in range(10):
        t = k / 9
        mx = x0 + L * 0.58 + (hx - x0 - L * 0.58) * t
        my = g - H * 0.65 + (hy + 60 - g + H * 0.65) * t
        mane.append(stroke_w([(mx - 20, my - 20), (mx + 30, my + 40), (mx + 10, my + 90)], [50, 36, 2]))
    mm = cv.polys_mask(mane)
    paint(cv, mm, P.WHITE_WARM, line=0.8, line_w=1.0, cel=0.5, soft=10, hi=0.4)
    p.tube([(hx - 90, hy + 70), (hx - 20, hy + 115), (hx + 60, hy + 95)], [24, 24, 24], P.hx("e2c98a"), cap=False)
    for zx in (hx - 50, hx + 15):
        p.shape([(zx - 9, hy + 118), (zx + 9, hy + 118), (zx + 9, hy + 138), (zx - 2, hy + 138), (zx - 2, hy + 158), (zx + 13, hy + 158),
                 (zx + 13, hy + 180), (zx - 7, hy + 180), (zx - 7, hy + 160), (zx - 9, hy + 160)], P.WHITE_WARM, smooth=False, shadow=0.15)
    # ---------------- head (elegant, facing right) with a vermilion spirit mask marking
    head = [(hx - 80, hy - 45), (hx + 5, hy - 75), (hx + 90, hy - 45), (hx + 190, hy + 25), (hx + 195, hy + 62), (hx + 135, hy + 80),
            (hx + 35, hy + 72), (hx - 70, hy + 40)]
    hm = p.shape(head, coat, n=6, soft=45, hi=0.5, light_col=P.WHITE_WARM)
    mk = cv.mask(catmull(A([(hx - 30, hy - 55), (hx + 70, hy - 38), (hx + 170, hy + 22), (hx + 70, hy + 6), (hx - 20, hy - 18)]), True, 5)) * hm
    cv.atop(P.VERMILION, mk * 0.65)
    p.ell(hx + 185, hy + 48, 15, 12, P.INK_SOFT, hi=0.6, gloss=0.5)
    for s in (-1, 1):
        p.shape([(hx - 40 + s * 22, hy - 40), (hx - 105 + s * 55, hy - 105), (hx - 30 + s * 45, hy - 48)], coat, n=4)
    ex, ey = hx + 62, hy - 2
    flat_fill(cv, cv.ellipse_mask(ex, ey, 26, 15, -0.2), P.hx("1e1a26"), 0.95)
    flat_fill(cv, cv.ellipse_mask(ex + 4, ey, 11, 8), P.BLIGHT_GLOW, 0.95)
    post_glow(cv, ex, ey, 110, P.BLIGHT_GLOW, 0.6)
    # ---------------- lanterns hanging from the antler tips
    from props import glow_pane
    for i, (lx, ly) in enumerate(lanterns):
        lit = i % 3 != 1
        p.lines([(lx, ly, lx, ly + 28)], 3, P.INK_SOFT, 0.9)
        bmask = cv.mask(catmull(A([(lx - 15, ly + 28), (lx + 15, ly + 28), (lx + 19, ly + 70), (lx - 19, ly + 70)]), True, 3))
        if lit:
            glow_pane(cv, bmask)
            post_glow(cv, lx, ly + 49, 70, P.LANTERN, 0.45)
        else:
            paint(cv, bmask, P.mix(P.INK, P.BLIGHT_VIOLET, 0.3), line=0.8, soft=3, ao=0)
            flat_fill(cv, cv.ellipse_mask(lx, ly + 49, 6, 10), P.BLIGHT_GLOW, 0.9)
            post_glow(cv, lx, ly + 49, 45, P.BLIGHT_GLOW, 0.35)
        p.shape([(lx - 20, ly + 30), (lx, ly + 17), (lx + 20, ly + 30)], P.INK_SOFT, smooth=False, shadow=0, line=0.6)
        p.shape([(lx - 17, ly + 69), (lx + 17, ly + 69), (lx + 12, ly + 77), (lx - 12, ly + 77)], P.INK_SOFT, smooth=False, shadow=0, line=0.6)
    from props import crystal_cluster
    crystal_cluster(cv, x0 - L * 0.05, g - H * 0.9, 100, r, key + "c1", n=4, glow_amt=0.9)
    crystal_cluster(cv, x0 + L * 0.2, g - H * 0.96, 70, r, key + "c2", n=3, glow_amt=0.9)
    for i in range(10):
        post_glow(cv, 120 + r.random() * 800, 150 + r.random() * 600, 10 + r.random() * 8, P.BLIGHT_GLOW, 0.8)
    return finish(cv, ow=3.0)


def cr_training_dummy(key="cr_training_dummy"):
    cv = new(key, 512, 1024)
    p = Painter(cv)
    g = 990
    cx = 256
    p.tube([(cx, g), (cx, g - 700)], [36, 32], P.WOOD, tex=cv.noise((30, 3), "w", 3))
    for s in (-1, 1):
        p.tube([(cx, g - 6), (cx + s * 120, g - 2)], [26, 22], P.WOOD_DK, cap=False)
    p.tube([(cx - 220, g - 560), (cx + 220, g - 560)], [30, 30], P.WOOD)
    straw = P.hx("e0c27a")
    for s in (-1, 1):
        m = p.shape([(cx + s * 170, g - 590), (cx + s * 250, g - 600), (cx + s * 265, g - 510), (cx + s * 175, g - 530)], straw, n=4)
        p.lines([(cx + s * (175 + k * 15), g - 595, cx + s * (180 + k * 16), g - 520) for k in range(6)], 2, P.THATCH_DK, 0.5, clip=m)
    body = [(cx - 120, g - 600), (cx + 120, g - 600), (cx + 140, g - 420), (cx + 100, g - 290), (cx - 100, g - 290), (cx - 140, g - 420)]
    bm = p.shape(body, straw, n=5, soft=50, hi=0.35)
    r = rng(key)
    segs = [(cx + (r.random() - 0.5) * 240, g - 580 + r.random() * 280, 0, 0) for _ in range(90)]
    segs = [(x, y, x + r.normal() * 4, y + 22) for x, y, _, _ in segs]
    p.lines(segs, 2, P.THATCH_DK, 0.45, clip=bm)
    for y in (g - 560, g - 330):
        p.tube([(cx - 130, y), (cx, y + 10), (cx + 130, y)], [14] * 3, P.hx("b8452f"), cap=False)
    for k, (rr, c) in enumerate(((70, P.WHITE_WARM), (52, P.hx("d9483b")), (34, P.WHITE_WARM), (16, P.hx("d9483b")))):
        p.ell(cx + 5, g - 450, rr, rr * 0.95, c, shadow=0.0, line=0.6, hi=0.2)
    hd = p.shape(ellipse(cx, g - 700, 95, 90, 0, 32), P.hx("d8c8a8"), smooth=False, soft=40, tex=cv.noise(5, "burlap", 2) * 0.4 + 0.3, hi=0.3)
    p.lines([(cx - 50, g - 715, cx - 20, g - 695), (cx - 50, g - 695, cx - 20, g - 715), (cx + 25, g - 715, cx + 55, g - 695),
             (cx + 25, g - 695, cx + 55, g - 715)], 6, P.INK_SOFT, 0.9)
    stitch = [(cx - 40 + k * 14, g - 660 + (k % 2) * 8, cx - 34 + k * 14, g - 652 - (k % 2) * 8) for k in range(6)]
    p.lines([(cx - 45, g - 655, cx + 45, g - 657)], 3, P.INK_SOFT, 0.8)
    p.lines(stitch, 2.5, P.INK_SOFT, 0.8)
    p.tube([(cx - 100, g - 740), (cx, g - 755), (cx + 100, g - 740)], [16] * 3, P.hx("b8452f"), cap=False)
    return finish(cv)


# ----------------------------------------------------------------------------------------
# pets / demons / totems
# ----------------------------------------------------------------------------------------
def pet_owl(key="pet_owl"):
    cv = new(key, 512, 512)
    p = Painter(cv)
    r = rng(key)
    g = 490
    cx = 250
    col, belly = P.hx("9a7452"), P.hx("f0dfc0")
    p.tube([(cx - 120, g - 20), (cx + 140, g - 26)], [30, 28], P.WOOD, cap=True)
    body = [(cx - 115, g - 60), (cx - 130, g - 200), (cx - 100, g - 300), (cx, g - 340), (cx + 100, g - 300), (cx + 130, g - 200), (cx + 115, g - 60),
            (cx, g - 30)]
    bm = p.shape(body, col, n=6, soft=60, hi=0.35)
    bel = cv.mask(catmull(A([(cx - 70, g - 70), (cx - 80, g - 200), (cx, g - 250), (cx + 80, g - 200), (cx + 70, g - 70), (cx, g - 45)]), True, 6)) * bm
    cv.atop(belly, bel * 0.9)
    chev = []
    for k in range(14):
        x = cx - 60 + (k % 5) * 30 + (k // 5 % 2) * 15
        y = g - 200 + (k // 5) * 45
        chev.append((x - 8, y, x, y + 8))
        chev.append((x, y + 8, x + 8, y))
    p.lines(chev, 3, P.mix(col, P.INK, 0.2), 0.7, clip=bel)
    for s in (-1, 1):
        w = [(cx + s * 95, g - 260), (cx + s * 145, g - 180), (cx + s * 130, g - 70), (cx + s * 100, g - 60), (cx + s * 90, g - 180)]
        wm = p.shape(w, P.mix(col, P.INK, 0.15), n=5)
        p.lines([(cx + s * 100, g - 200 + k * 30, cx + s * 135, g - 190 + k * 30) for k in range(4)], 3, P.mix(col, P.INK, 0.4), 0.6, clip=wm)
    face = cv.mask(catmull(A([(cx - 100, g - 270), (cx, g - 320), (cx + 100, g - 270), (cx + 90, g - 200), (cx, g - 215), (cx - 90, g - 200)]), True, 6))
    cv.atop(P.mix(belly, col, 0.15), face * bm * 0.9)
    for s in (-1, 1):
        p.shape([(cx + s * 60, g - 310), (cx + s * 110, g - 380), (cx + s * 100, g - 300)], col, n=3)
        p.eye(cx + s * 48 + 8, g - 262, 34, 34, P.hx("f2b33d"), look=(0.15, 0.05), lid=False)
    p.shape([(cx - 10, g - 245), (cx + 22, g - 245), (cx + 6, g - 210)], P.hx("e8a03a"), smooth=False, shadow=0.15)
    for s in (-1, 1):
        for k in range(3):
            p.tube([(cx + s * 40 + (k - 1) * 12, g - 45), (cx + s * 40 + (k - 1) * 16, g - 18)], [9, 4], P.hx("e8a03a"))
    return finish(cv)


def demon_imp(key="demon_imp"):
    cv = new(key, 512, 512)
    p = Painter(cv)
    g = 490
    cx = 240
    skin, dk = P.hx("e0603a"), P.hx("9a3326")
    # tail and wings
    p.tube([(cx - 40, g - 140), (cx - 140, g - 100), (cx - 170, g - 180)], [18, 12, 5], dk)
    p.shape([(cx - 170, g - 180), (cx - 200, g - 215), (cx - 150, g - 205)], dk, smooth=False)
    for s, sc in ((-1, 1.0), (1, 0.8)):
        w = [(cx + s * 30, g - 260), (cx + s * 150 * sc, g - 360), (cx + s * 170 * sc, g - 280), (cx + s * 120 * sc, g - 300),
             (cx + s * 130 * sc, g - 240), (cx + s * 80 * sc, g - 250)]
        p.shape(w, P.mix(dk, P.VIOLET, 0.3), smooth=False, hi=0.3)
    for s in (-1, 1):
        p.tube([(cx + s * 35, g - 140), (cx + s * 50, g - 80), (cx + s * 40, g - 20)], [36, 26, 22], skin)
        p.ell(cx + s * 40 + 10, g - 16, 26, 14, dk)
    body = [(cx - 70, g - 270), (cx + 70, g - 270), (cx + 80, g - 180), (cx + 50, g - 120), (cx - 50, g - 120), (cx - 80, g - 180)]
    p.shape(body, skin, n=5, soft=30)
    p.shape([(cx - 60, g - 150), (cx + 60, g - 150), (cx + 55, g - 110), (cx - 55, g - 110)], P.hx("3a2a22"), n=4)
    for s in (-1, 1):
        p.tube([(cx + s * 65, g - 250), (cx + s * 110, g - 200), (cx + s * 120 if s < 0 else cx + 150, g - 160 if s < 0 else g - 280)], [26, 22, 18], skin)
    # fireball in raised hand
    fx, fy = cx + 160, g - 320
    p.ell(fx, fy, 40, 40, P.AMBER, cel=0.3, hi=0.8, light_col=P.LANTERN_CORE, line=0.5)
    flat_fill(cv, cv.ellipse_mask(fx - 6, fy - 6, 20, 20), P.LANTERN_CORE, 0.9)
    post_glow(cv, fx, fy, 110, P.AMBER, 0.55)
    hx, hy = cx + 10, g - 340
    for s in (-1, 1):
        p.shape([(hx + s * 60, hy - 10), (hx + s * 150, hy - 50), (hx + s * 80, hy + 25)], skin, n=4)
        p.shape([(hx + s * 30, hy - 60), (hx + s * 55, hy - 130), (hx + s * 50, hy - 55)], P.hx("f2e2c0"), n=4)
    p.shape(ellipse(hx, hy, 80, 72, 0, 32), skin, smooth=False, soft=30, hi=0.4)
    for s in (-1, 1):
        p.eye(hx + s * 30 + 10, hy - 12, 18, 15, P.hx("f6e04a"), slit=True, lid=True)
    grin = cv.mask(catmull(A([(hx - 35, hy + 22), (hx + 50, hy + 18), (hx + 10, hy + 45)]), True, 5))
    flat_fill(cv, grin, P.hx("3a1a1a"), 0.95)
    for k in range(4):
        x = hx - 22 + k * 18
        flat_fill(cv, cv.mask(A([(x - 5, hy + 22), (x + 5, hy + 21), (x, hy + 30)])), P.WHITE_WARM, 0.95)
    return finish(cv)


def demon_voidwalker(key="demon_voidwalker"):
    cv = new(key, 512, 1024)
    p = Painter(cv)
    g = 990
    cx = 250
    col = P.hx("5a5aa8")
    dk = P.hx("2e2a5a")
    # smoky tail instead of legs
    tail = [(cx - 110, g - 420), (cx + 110, g - 420), (cx + 70, g - 250), (cx + 20, g - 120), (cx + 40, g - 30), (cx - 30, g - 80), (cx - 60, g - 220)]
    tm = p.shape(tail, P.mix(col, dk, 0.5), n=5, cel=0.3, soft=60)
    wisp = ragged(cv, tm, 0.6, 3, 10, "w")
    torso = [(cx - 170, g - 760), (cx + 170, g - 760), (cx + 150, g - 600), (cx + 100, g - 420), (cx - 100, g - 420), (cx - 150, g - 600)]
    bm = p.shape(torso, col, n=5, soft=70, cel=0.5, hi=0.5, light_col=P.hx("a8b0f0"))
    swirl = cv.blur(cv.ellipse_mask(cx, g - 560, 70, 90), 30) * bm
    cv.atop(P.hx("1a1838"), swirl * 0.6)
    for s in (-1, 1):
        sh = (cx + s * 160, g - 730)
        el = (cx + s * 230, g - 580)
        wr = (cx + s * 220, g - 440)
        p.tube([sh, el, wr], [110, 80, 70], col, cel=0.5, hi=0.5)
        p.tube([(el[0] + (wr[0] - el[0]) * 0.4, el[1] + (wr[1] - el[1]) * 0.4), wr], [96, 92], GOLD, cap=False, hi=0.6, gloss=0.4)
        p.shape(ellipse(wr[0], wr[1] + 50, 70, 60, 0, 28), col, smooth=False, hi=0.5)
        for k in range(3):
            p.shape([(wr[0] - 40 + k * 40, wr[1] + 95), (wr[0] - 30 + k * 40, wr[1] + 140), (wr[0] - 20 + k * 40, wr[1] + 95)], dk, smooth=False, shadow=0)
    # head: hood-like shape with glowing eyes
    hx, hy = cx + 10, g - 830
    p.shape([(hx - 110, hy + 60), (hx - 90, hy - 50), (hx, hy - 100), (hx + 90, hy - 50), (hx + 110, hy + 60), (hx, hy + 80)], col, n=5, hi=0.5)
    fm = cv.mask(catmull(A([(hx - 60, hy + 40), (hx - 50, hy - 20), (hx + 50, hy - 20), (hx + 60, hy + 40), (hx, hy + 55)]), True, 5))
    flat_fill(cv, fm, dk, 0.95)
    for s in (-1, 1):
        flat_fill(cv, cv.ellipse_mask(hx + s * 26 + 6, hy + 10, 14, 8), P.hx("f2f0ff"), 0.95)
        post_glow(cv, hx + s * 26 + 6, hy + 10, 50, P.hx("a8b0f0"), 0.5)
    p.tube([(cx - 150, g - 770), (cx, g - 740), (cx + 150, g - 770)], [40] * 3, GOLD, cap=False, hi=0.6, gloss=0.4)
    return finish(cv)


def demon_infernal(key="demon_infernal"):
    """Hulking burning stone golem demon (Warlock's Inferno)."""
    cv = new(key, 1024, 1024)
    p = Painter(cv, scale=1.4)
    r = rng(key)
    g = 1000
    cx = 500
    stone = P.hx("4a4048")
    stone_lt = P.hx("6e6268")
    lava = P.hx("ff8a2a")

    def rock(pts, col=stone, key2="rk"):
        m = p.shape(pts, col, n=3, soft=30, cel=0.75, hi=0.5, light_col=stone_lt, line=0.95, line_w=1.4)
        cracks = cv.noise((26, 26), key2 + "ck", 3)
        ck = m * smoothstep(0.03, 0.0, np.abs(cracks - 0.5)) * (m > 0.95)
        cv.atop(lava, ck.astype(F32) * 0.95)
        cv.atop(P.LANTERN, (ck * 0.6).astype(F32))
        return m
    # legs
    for s in (-1, 1):
        rock([(cx + s * 40, g - 420), (cx + s * 190, g - 400), (cx + s * 220, g - 200), (cx + s * 240, g - 10), (cx + s * 60, g - 5), (cx + s * 50, g - 200)],
             key2="lg%d" % s)
    # torso
    torso = [(cx - 260, g - 780), (cx - 120, g - 840), (cx + 120, g - 840), (cx + 270, g - 770), (cx + 230, g - 560), (cx + 160, g - 420),
             (cx - 160, g - 420), (cx - 230, g - 560)]
    rock(torso, key2="tr")
    core = cv.blur(cv.ellipse_mask(cx, g - 620, 70, 80), 20) * (cv.a > 0.9)
    cv.atop(lava, core * 0.95)
    cv.atop(P.LANTERN_CORE, cv.blur(cv.ellipse_mask(cx, g - 620, 30, 36), 10) * 0.9)
    post_glow(cv, cx, g - 620, 200, P.AMBER, 0.35)
    # arms (massive)
    for s in (-1, 1):
        rock([(cx + s * 230, g - 800), (cx + s * 380, g - 760), (cx + s * 420, g - 560), (cx + s * 330, g - 540), (cx + s * 260, g - 640)], key2="ua%d" % s)
        rock([(cx + s * 330, g - 580), (cx + s * 430, g - 560), (cx + s * 470, g - 330), (cx + s * 330, g - 300), (cx + s * 320, g - 420)], key2="fa%d" % s)
        fist = [(cx + s * 300, g - 340), (cx + s * 470, g - 340), (cx + s * 490, g - 230), (cx + s * 400, g - 180), (cx + s * 310, g - 220)]
        rock(fist, col=P.mix(stone, P.INK, 0.15), key2="fi%d" % s)
    # head small, sunk between shoulders, flaming crown
    hx, hy = cx + 20, g - 860
    flames = []
    for k in range(7):
        x = hx - 120 + k * 40
        h = 140 + 70 * math.sin(k / 6 * math.pi) + r.random() * 40
        flames.append(catmull(A([(x - 35, hy + 20), (x - 10, hy - h * 0.6), (x + 5, hy - h), (x + 25, hy - h * 0.5), (x + 35, hy + 20)]), True, 4))
    fm = cv.polys_mask(flames)
    flat_fill(cv, fm, P.hx("e8743b"), 0.95)
    flat_fill(cv, cv.polys_mask([np.asarray(f_) * [1, 1] for f_ in flames]) * smoothstep(hy - 60, hy + 20, cv.yy()), P.AMBER, 0.9)
    rock([(hx - 100, hy + 60), (hx - 90, hy - 30), (hx, hy - 60), (hx + 90, hy - 30), (hx + 100, hy + 60), (hx, hy + 90)], key2="hd")
    for s in (-1, 1):
        flat_fill(cv, cv.ellipse_mask(hx + s * 38 + 10, hy + 5, 20, 12), P.LANTERN_CORE, 0.95)
        post_glow(cv, hx + s * 38 + 10, hy + 5, 60, P.AMBER, 0.5)
    post_glow(cv, hx, hy - 80, 220, P.AMBER, 0.3)
    return finish(cv, ow=3.0)


def totem(key, element):
    cv = new(key, 256, 512)
    p = Painter(cv)
    g = 496
    cx = 128
    wood = {"earth": P.hx("8a6a45"), "fire": P.hx("9a5a3a"), "water": P.hx("6a7a8a"), "air": P.hx("b8b0a0")}[element]
    acc = {"earth": P.hx("6f9a3c"), "fire": P.hx("e8743b"), "water": P.hx("4f9ad0"), "air": P.hx("bfe6f0")}[element]
    # base mound
    p.shape([(cx - 70, g), (cx - 50, g - 30), (cx + 50, g - 30), (cx + 70, g)], P.STONE_WARM, n=4)
    pole = [(cx - 40, g - 25), (cx - 42, g - 300), (cx + 42, g - 300), (cx + 40, g - 25)]
    pm = p.shape(pole, wood, smooth=False, soft=20, tex=cv.noise((30, 3), "w", 3), hi=0.35)
    # carved faces
    for k, fy in enumerate((g - 110, g - 220)):
        p.lines([(cx - 42, fy - 45, cx + 42, fy - 45)], 3, P.shadow_of(wood, 1.4), 0.8, clip=pm)
        for s in (-1, 1):
            p.ell(cx + s * 16, fy - 18, 9, 7, P.mix(P.INK, wood, 0.2), line=0, shadow=0)
        p.shape([(cx - 18, fy + 5), (cx + 18, fy + 5), (cx + 12, fy + 22), (cx - 12, fy + 22)], acc, smooth=False, shadow=0, line=0.7)
        p.shape([(cx - 4, fy - 12), (cx + 4, fy - 12), (cx + 6, fy + 2), (cx - 6, fy + 2)], P.shadow_of(wood, 1.0), smooth=False, shadow=0, line=0.5)
    # element crown
    ty = g - 310
    if element == "earth":
        for k, (dx, h) in enumerate(((-35, 70), (0, 100), (35, 65))):
            p.shape([(cx + dx - 26, ty + 10), (cx + dx - 10, ty - h), (cx + dx + 14, ty - h * 0.8), (cx + dx + 26, ty + 10)], P.mix(P.STONE, acc, 0.15),
                    smooth=False, hi=0.5)
        p.shape([(cx - 30, ty - 10), (cx - 60, ty - 40), (cx - 20, ty - 30)], acc, n=3)
    elif element == "fire":
        from props import flame
        flame(cv, cx, ty + 10, 110, 150, rng(key), key)
        post_glow(cv, cx, ty - 50, 120, P.AMBER, 0.45)
    elif element == "water":
        p.ell(cx, ty - 50, 48, 48, acc, cel=0.4, hi=0.8, gloss=0.7, light_col=P.WHITE_WARM)
        for s in (-1, 1):
            p.tube([(cx + s * 50, ty), (cx + s * 80, ty - 40), (cx + s * 60, ty - 80), (cx + s * 40, ty - 70)], [16, 14, 10, 4], P.mix(acc, P.TEAL, 0.4))
        post_glow(cv, cx, ty - 50, 90, acc, 0.35)
    else:
        for s in (-1, 1):
            w = [(cx, ty), (cx + s * 100, ty - 80), (cx + s * 110, ty - 40), (cx + s * 80, ty - 30), (cx + s * 90, ty - 5)]
            wm = p.shape(w, P.WHITE_WARM, n=4, hi=0.4)
            p.lines([(cx + s * 30, ty - 15, cx + s * 95, ty - 60), (cx + s * 30, ty - 5, cx + s * 85, ty - 25)], 2, P.mix(acc, P.DUSTY_BLUE, 0.5), 0.6,
                    clip=wm)
        p.ell(cx, ty - 20, 30, 30, acc, hi=0.7, gloss=0.5)
    # binding cords + feathers
    p.tube([(cx - 44, g - 165), (cx + 44, g - 160)], [10, 10], P.hx("e8d8b8"), cap=False)
    p.shape([(cx + 44, g - 162), (cx + 70, g - 120), (cx + 62, g - 70), (cx + 50, g - 120)], acc, n=4)
    return finish(cv)


# ----------------------------------------------------------------------------------------
# humanoid creatures on the character rig
# ----------------------------------------------------------------------------------------
def mask_scarf(f, col):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    pts = [(hx - hw * 1.05, hy + hh * 0.2), (hx + hw * 1.02, hy + hh * 0.18), (hx + hw * 0.9, hy + hh * 0.7), (hx + hw * 0.3, hy + hh * 1.08),
           (hx - hw * 0.4, hy + hh * 1.05), (hx - hw * 0.95, hy + hh * 0.7)]
    m = f.shape(pts, col, n=5)
    f.folds(m, [(hx - hw * 0.7, hy + hh * 0.45, hx + hw * 0.6, hy + hh * 0.5), (hx - hw * 0.5, hy + hh * 0.75, hx + hw * 0.4, hy + hh * 0.8)], col, 0.5,
            hw * 0.04)


def hood(f, col, back):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    if back:
        f.shape([(hx - hw * 1.3, hy + hh * 0.9), (hx - hw * 1.35, hy - hh * 0.4), (hx - hw * 0.7, hy - hh * 1.25), (hx + hw * 0.3, hy - hh * 1.4),
                 (hx + hw * 1.2, hy - hh * 0.9), (hx + hw * 1.35, hy + hh * 0.3), (hx + hw * 1.25, hy + hh * 0.95)], P.mix(col, P.INK, 0.35), n=5, shadow=0)
        return
    pts = [(hx - hw * 1.3, hy + hh * 0.9), (hx - hw * 1.35, hy - hh * 0.4), (hx - hw * 0.7, hy - hh * 1.25), (hx + hw * 0.3, hy - hh * 1.4),
           (hx + hw * 1.2, hy - hh * 0.9), (hx + hw * 1.35, hy + hh * 0.3), (hx + hw * 1.25, hy + hh * 0.95), (hx + hw * 0.95, hy + hh * 0.3),
           (hx + hw * 0.8, hy - hh * 0.5), (hx, hy - hh * 0.85), (hx - hw * 0.85, hy - hh * 0.45), (hx - hw * 1.0, hy + hh * 0.3)]
    f.shape(pts, col, n=4)


def bandit(key, kind):
    cv = C.new_canvas(key)
    if kind == "chief":
        look = Look(build="big", height=2.0, heads=7.0, hair=P.hx("5a2e22"), beard=P.hx("6a3424"), beard_len=1.0, eyes=P.hx("6a4a3a"),
                    skin=P.SKIN_TAN, hair_style="mohawk", expr="grin", turn=0.15)
    elif kind == "hexer":
        look = Look(height=1.78, hair=P.hx("3a3a3a"), eyes=P.hx("b98cff"), skin=P.SKIN_LIGHT, hair_style="none", expr="stern")
    else:
        look = Look(height=1.8, hair=P.hx("4a3328"), eyes=P.hx("8a6a3a"), skin=P.SKIN_LIGHT, hair_style="none", expr="stern")
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    cloak = {"cutthroat": P.hx("5a4a3e"), "archer": P.hx("4f5a3a"), "hexer": P.hx("4a3a52"), "chief": P.hx("6a3a2e")}[kind]
    scarf_c = {"cutthroat": P.hx("8a2e2e"), "archer": P.hx("6a6a3a"), "hexer": P.hx("3a2a3a"), "chief": P.hx("8a2e2e")}[kind]
    if kind == "archer":
        quiver(f, f.shR[0] - hu * 0.2, f.sh_y + hu * 0.55, math.radians(25), LEATHER_DK)
    if kind in ("cutthroat", "archer", "hexer"):
        hood(f, cloak, back=True)
        C.cape_back(f, cloak, hem_dy=0.6 if kind != "hexer" else 1.6, w=1.35)
    if kind == "chief":
        C.cape_back(f, P.hx("4a2e22"), hem_dy=0.3, w=1.6)
    for s in (-1, 1):
        f.leg(s, P.hx("4a4038"), w_scale=1.0)
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.knee_y + hu * 0.05, cuff=LEATHER)
        knee = f.kneeL if s < 0 else f.kneeR
        f.band([(knee[0] - hu * 0.2, knee[1] + hu * 0.4), (knee[0] + hu * 0.2, knee[1] + hu * 0.45)], hu * 0.06, LEATHER, cap=False)
    if kind == "hexer":
        C.robe(f, cloak, w_hem=1.4, sash=P.hx("6a4a2a"), trim=P.hx("8a7a4a"))
    else:
        f.neck()
        f.torso(P.hx("6a5a48"))
        C.vest(f, LEATHER if kind != "chief" else P.hx("4a3a30"), open_w=0.15, hem_y=f.hip_y + hu * 0.25, trim=None)
        f.belt(f.waist_y + hu * 0.12, hu * 0.18, LEATHER_DK, buckle=P.mix(P.HONEY, P.STONE, 0.4))
        f.band([(cx - f.w_sh * 0.7, f.sh_y + hu * 0.1), (cx + f.w_waist * 0.9, f.waist_y + hu * 0.05)], hu * 0.09, LEATHER_DK)
    if kind == "chief":
        fur(f, [(cx - f.w_sh * 1.15, f.sh_y + hu * 0.4), (cx - f.w_sh * 0.7, f.sh_y - hu * 0.05), (cx, f.sh_y + hu * 0.1), (cx + f.w_sh * 0.7, f.sh_y - hu * 0.05),
                (cx + f.w_sh * 1.15, f.sh_y + hu * 0.4)], hu * 0.45, P.hx("8a7a6a"), key="mantle")
        f.pauldron(1, STEEL_DK, layers=2, size=1.1)
    # arms & weapons
    if kind == "cutthroat":
        wr = (f.shL[0] - hu * 0.35, f.hip_y + hu * 0.05)
        f.arm(-1, (f.shL[0] - hu * 0.3, f.shL[1] + hu * 1.0), wr, sleeve=P.hx("6a5a48"), glove=LEATHER_DK)
        dagger(f, (wr[0], wr[1] + hu * 0.1), (wr[0] - hu * 0.15, wr[1] + hu * 0.95))
        f.hand(wr[0], wr[1], 0.1, fist=True, col=LEATHER_DK)
        wr = (f.shR[0] + hu * 0.3, f.chest_y + hu * 0.4)
        f.arm(1, (f.shR[0] + hu * 0.45, f.shR[1] + hu * 0.9), wr, sleeve=P.hx("6a5a48"), glove=LEATHER_DK)
        dagger(f, wr, (wr[0] + hu * 0.75, wr[1] - hu * 0.35))
        f.hand(wr[0], wr[1], -1.9, fist=True, col=LEATHER_DK)
    elif kind == "archer":
        wr = (f.shL[0] - hu * 0.5, f.hip_y - hu * 0.1)
        f.arm(-1, (f.shL[0] - hu * 0.4, f.shL[1] + hu * 1.0), wr, sleeve=P.hx("6a5a48"), glove=LEATHER_DK)
        bow(f, wr[0] + hu * 0.05, f.sh_y + hu * 0.1, f.knee_y - hu * 0.1, col=P.WOOD_DK, side=-1, bulge=0.15)
        f.hand(wr[0], wr[1], 0.0, fist=True, col=LEATHER_DK)
        wr = (f.shR[0] + hu * 0.25, f.shR[1] + hu * 2.0)
        f.arm(1, (f.shR[0] + hu * 0.2, f.shR[1] + hu * 1.05), wr, sleeve=P.hx("6a5a48"), glove=LEATHER_DK)
        f.hand(wr[0], wr[1], 0.0, fist=True, col=LEATHER_DK)
    elif kind == "hexer":
        sx = f.shR[0] + hu * 0.5
        f.band([(sx, f.feet - hu * 0.05), (sx - hu * 0.05, f.top + hu * 0.2)], hu * 0.09, P.WOOD_DK)
        f.ell(sx - hu * 0.05, f.top + hu * 0.1, hu * 0.22, hu * 0.2, P.hx("efe6d2"), hi=0.4)
        for s in (-1, 1):
            f.ell(sx - hu * 0.05 + s * hu * 0.08, f.top + hu * 0.08, hu * 0.05, hu * 0.06, P.INK, line=0, shadow=0)
        for k in range(4):
            yy = f.top + hu * (0.5 + k * 0.25)
            C.totem_charm(f, sx + hu * 0.18 + (k % 2) * hu * 0.1, yy, hu * 0.3, [P.hx("efe6d2"), P.WOOD, P.hx("8a3a5a")][k % 3])
        hand = (sx - hu * 0.02, f.waist_y)
        C.wide_sleeve_arm(f, 1, (f.shR[0] + hu * 0.3, f.shR[1] + hu * 1.0), hand, cloak, cuff=P.hx("8a7a4a"))
        wr = (cx - f.w_chest * 0.6, f.chest_y + hu * 0.3)
        C.wide_sleeve_arm(f, -1, (f.shL[0] - hu * 0.3, f.shL[1] + hu * 0.95), wr, cloak, cuff=P.hx("8a7a4a"), hand_fist=False, hand_ang=-2.6)
        hx_, hy_ = wr[0] - hu * 0.05, wr[1] - hu * 0.4
        f.ell(hx_, hy_, hu * 0.18, hu * 0.18, P.hx("8a5ad0"), cel=0.3, hi=0.8, light_col=P.BLIGHT_GLOW, line=0.5)
        f.glow(hx_, hy_, hu * 0.8, P.hx("b98cff"), 0.5)
        C.necklace(f, [P.hx("efe6d2"), P.hx("8a3a5a")], y_off=0.35, n=7, r=0.06, sag=0.6)
    else:  # chief: big axe on shoulder
        hand = (cx + f.w_sh * 0.4, f.chest_y + hu * 0.25)
        f.arm(1, (f.shR[0] + hu * 0.3, f.waist_y - hu * 0.05), hand, sleeve=None, glove=LEATHER_DK)
        d = (math.sin(math.radians(25)), -math.cos(math.radians(25)))
        top = (hand[0] + d[0] * hu * 2.2, hand[1] + d[1] * hu * 2.2)
        f.band([(hand[0] - d[0] * hu * 0.6, hand[1] - d[1] * hu * 0.6), top], hu * 0.11, P.WOOD)
        ax = [(top[0] - hu * 0.1, top[1] + hu * 0.1), (top[0] + hu * 0.75, top[1] - hu * 0.2), (top[0] + hu * 0.95, top[1] + hu * 0.4),
              (top[0] + hu * 0.65, top[1] + hu * 0.75), (top[0] + hu * 0.05, top[1] + hu * 0.45)]
        f.shape(ax, STEEL, n=4, hi=0.6, gloss=0.4)
        f.hand(hand[0], hand[1] - hu * 0.02, -2.6, fist=True, col=LEATHER_DK)
        wr = (f.shL[0] - hu * 0.3, f.hip_y + hu * 0.05)
        f.arm(-1, (f.shL[0] - hu * 0.35, f.shL[1] + hu * 1.0), wr, sleeve=None, glove=LEATHER_DK)
        f.hand(wr[0], wr[1], 0.0, fist=True, col=LEATHER_DK)
    # head
    hair_back = kind == "chief"
    if hair_back:
        human_head(f, back_only=True)
    f.ears()
    f.face()
    f.features()
    if kind == "chief":
        C.beard(f, look.beard, length=1.0)
        C.hair_style(f, "mohawk", back=False)
        hx, hy = f.head_c
        f.band([(hx - f.hw * 1.0, hy - f.hh * 0.3), (hx + f.hw * 0.9, hy + f.hh * 0.05)], f.hh * 0.06, P.INK_SOFT, cap=False)
        f.ell(hx - f.hw * 0.3, hy + f.hh * 0.18, f.hw * 0.24, f.hh * 0.17, P.INK_SOFT, hi=0.4)
    else:
        mask_scarf(f, scarf_c)
        hood(f, cloak, back=False)
    return {key: C.finish_char(cv)[0]}


def hollow_spirit(key="cr_hollow_spirit"):
    cv = C.new_canvas(key)
    look = Look(height=1.9, heads=7.0, skin=P.hx("d8d4e4"), hair=P.hx("8a849a"), eyes=P.BLIGHT_GLOW, hair_style="long", turn=0.1)
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    robe_c = P.hx("7a7290")
    hx, hy = f.head_c
    f.paint_hair(C.back_mass(f, f.waist_y + hu * 0.5, width=1.5, n=9, jag=0.25), look.hair, shine=False, each=False)
    # floating tattered robe (no legs)
    hem = f.knee_y + hu * 0.9
    pts = [(cx - f.w_sh * 0.95, f.sh_y + hu * 0.1), (cx + f.w_sh * 0.95, f.sh_y + hu * 0.1), (cx + f.w_hip * 1.35, f.hip_y), (cx + f.w_hip * 1.6, hem - hu * 0.2)]
    r = rng(key)
    n = 9
    for k in range(n + 1):
        t = k / n
        x = cx + f.w_hip * 1.6 - t * f.w_hip * 3.2
        y = hem + (hu * 0.6 if k % 2 else -hu * 0.1) + r.random() * hu * 0.3
        pts.append((x, y))
    pts.append((cx - f.w_hip * 1.35, f.hip_y))
    m = f.shape(pts, robe_c, n=3, soft=hu * 0.4)
    fade = m * smoothstep(f.knee_y, hem + hu * 0.6, cv.yy())
    cv.a *= (1 - fade * 0.65)
    cv.rgb *= (1 - fade * 0.65)[..., None]
    f.folds(m, [(cx + k * hu * 0.3, f.hip_y, cx + k * hu * 0.45, hem) for k in (-2, -1, 0, 1, 2)], robe_c, 0.45, hu * 0.04)
    f.belt(f.waist_y + hu * 0.1, hu * 0.14, P.hx("4a4258"), buckle=None)
    C.runes(f, (m * (cv.a > 0.95)).astype(F32), P.BLIGHT_GLOW, n=6, s=0.12, key="hs")
    # long thin arms reaching forward
    for s in (-1, 1):
        sh = f.shL if s < 0 else f.shR
        el = (sh[0] + s * hu * 0.35, sh[1] + hu * 0.9)
        wr = (sh[0] + s * hu * 0.25 + hu * 0.45, sh[1] + hu * 1.5)
        C.wide_sleeve_arm(f, s, el, wr, robe_c, hand_fist=False, hand_ang=-0.8 if s > 0 else 0.6)
    f.neck()
    # pale mask face with hollow eyes
    mm = f.shape(f.face_pts(), P.hx("eeeaf2"), shadow=0, cel=0.6, hi=0.3)
    for (ex, ey, sx) in ((hx - f.hw * 0.4, hy + f.hh * 0.12, 1.0), (hx + f.hw * 0.45, hy + f.hh * 0.1, 0.9)):
        flat_fill(cv, cv.ellipse_mask(ex, ey, f.hw * 0.2 * sx, f.hh * 0.18), P.hx("1e1a26"), 0.95)
        flat_fill(cv, cv.ellipse_mask(ex + f.hw * 0.03, ey, f.hw * 0.07, f.hh * 0.07), P.BLIGHT_GLOW, 0.95)
        f.glow(ex, ey, hu * 0.4, P.BLIGHT_GLOW, 0.4)
    f.lines([(hx - f.hw * 0.15, hy + f.hh * 0.6, hx + f.hw * 0.2, hy + f.hh * 0.62)], f.hw * 0.04, P.hx("4a4258"), 0.8)
    for k in range(3):
        f.lines([(hx - f.hw * 0.4 + k * f.hw * 0.05, hy - f.hh * 0.6, hx - f.hw * 0.2 + k * f.hw * 0.1, hy - f.hh * 0.1)], f.hw * 0.03, P.BLIGHT_DK, 0.5,
                clip=mm)
    hood(f, robe_c, back=False)
    cv.atop(P.BLIGHT_VIOLET, cv.a * 0.08)
    f.glow(cx, f.waist_y, hu * 2.5, P.BLIGHT_GLOW, 0.15)
    return {key: C.finish_char(cv)[0]}


def succubus(key="demon_succubus"):
    cv = C.new_canvas(key)
    look = Look(build="f", height=1.86, skin=P.hx("c8a8d8"), hair=P.hx("2a1e30"), eyes=P.hx("e8b04f"), hair_style="long", expr="smile", turn=0.2)
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    wing = P.hx("4a2a52")
    for s in (-1, 1):
        base = (cx + s * f.w_sh * 0.5, f.sh_y + hu * 0.3)
        pts = [base, (cx + s * f.w_sh * 2.3, f.top - hu * 0.2), (cx + s * f.w_sh * 2.7, f.sh_y + hu * 0.4), (cx + s * f.w_sh * 2.1, f.sh_y + hu * 0.2),
               (cx + s * f.w_sh * 2.2, f.chest_y + hu * 0.6), (cx + s * f.w_sh * 1.6, f.chest_y), (cx + s * f.w_sh * 1.4, f.waist_y), (cx + s * f.w_sh * 0.8, f.chest_y)]
        wm = f.shape(pts, wing, smooth=False, hi=0.3, shadow=0)
        f.lines([(base[0], base[1], cx + s * f.w_sh * 2.3, f.top - hu * 0.2), (base[0], base[1], cx + s * f.w_sh * 2.1, f.sh_y + hu * 0.2),
                 (base[0], base[1], cx + s * f.w_sh * 1.6, f.chest_y)], hu * 0.04, P.mix(wing, P.INK, 0.4), 0.9)
    C.hair_style(f, "long", back=True)
    f.tube([(cx + f.w_hip * 0.5, f.hip_y + hu * 0.3), (cx + f.w_hip * 1.6, f.knee_y), (cx + f.w_hip * 1.2, f.ankle_y - hu * 0.3)],
           [hu * 0.1, hu * 0.07, hu * 0.04], look.skin)
    f.shape([(cx + f.w_hip * 1.2, f.ankle_y - hu * 0.3), (cx + f.w_hip * 1.0, f.ankle_y - hu * 0.05), (cx + f.w_hip * 1.4, f.ankle_y - hu * 0.15)],
            P.hx("8a3a6a"), smooth=False)
    for s in (-1, 1):
        f.boot(s, P.hx("2a1e30"), top_y=f.knee_y - hu * 0.2, toe=1.2)
    gown = P.hx("5a2a5a")
    C.skirt(f, f.waist_y, f.ankle_y + hu * 0.1, f.w_hip * 0.95, f.w_hip * 1.5, gown, folds=5, front_split=0.35, trim=P.hx("c9a85a"))
    f.neck()
    t = f.torso(gown, neckline=0.5)
    f.shape([(cx - f.w_waist * 1.05, f.chest_y + hu * 0.35), (cx + f.w_waist * 1.05, f.chest_y + hu * 0.35), (cx + f.w_waist * 1.05, f.waist_y + hu * 0.15),
             (cx - f.w_waist * 1.05, f.waist_y + hu * 0.15)], P.hx("2a1e30"), smooth=False, hi=0.5, gloss=0.3)
    for s in (-1, 1):
        f.pauldron(s, P.hx("2a1e30"), layers=2, size=0.8, trim=P.hx("c9a85a"))
    # whip in the right hand
    wr = (f.shR[0] + hu * 0.4, f.hip_y)
    f.arm(1, (f.shR[0] + hu * 0.3, f.shR[1] + hu * 1.0), wr, glove=P.hx("2a1e30"))
    whip = [(wr[0], wr[1]), (wr[0] + hu * 0.5, wr[1] + hu * 0.6), (wr[0] + hu * 0.2, f.knee_y + hu * 0.3), (wr[0] + hu * 0.7, f.ankle_y)]
    f.tube(whip, [hu * 0.06, hu * 0.05, hu * 0.035, hu * 0.02], P.hx("6a3a5a"), shadow=0.15)
    f.hand(wr[0], wr[1], 0.0, fist=True, col=P.hx("2a1e30"))
    wr = (cx - f.w_waist * 1.0, f.waist_y + hu * 0.05)
    f.arm(-1, (f.shL[0] - hu * 0.35, f.shL[1] + hu * 0.85), wr, glove=P.hx("2a1e30"))
    f.hand(wr[0], wr[1], 2.4, fist=True, col=P.hx("2a1e30"))
    human_head(f)
    hx, hy = f.head_c
    for s in (-1, 1):
        f.tube([(hx + s * f.hw * 0.5, hy - f.hh * 0.8), (hx + s * f.hw * 0.9, hy - f.hh * 1.4), (hx + s * f.hw * 0.75, hy - f.hh * 1.75)],
               [f.hw * 0.22, f.hw * 0.14, f.hw * 0.04], P.hx("2a1e30"), hi=0.5)
    return {key: C.finish_char(cv)[0]}


# ----------------------------------------------------------------------------------------
# registration
# ----------------------------------------------------------------------------------------
def _one(fn, key, *a):
    return {key: fn(key, *a)}


def _quad(key, kind):
    return {key: quad_animal(key, kind)}


CREATURES = [
    # key, fn, args, size, measured height (None => character canvas height), pivot y
    ("cr_wolf", _quad, ("wolf",), (1024, 512), 1.1),
    ("cr_wolf_blighted", _quad, ("wolf_blighted",), (1024, 512), 1.2),
    ("cr_boar", _quad, ("boar",), (1024, 512), 1.0),
    ("cr_spider", _one, (cr_spider,), (1024, 512), 0.9),
    ("cr_mossling", _one, (lambda k: mossling(k, False),), (512, 512), 0.9),
    ("cr_mossling_shaman", _one, (lambda k: mossling(k, True),), (512, 512), 1.0),
    ("cr_bandit", bandit, ("cutthroat",), (512, 1024), None),
    ("cr_bandit_archer", bandit, ("archer",), (512, 1024), None),
    ("cr_bandit_hexer", bandit, ("hexer",), (512, 1024), None),
    ("cr_bandit_chief", bandit, ("chief",), (512, 1024), None),
    ("cr_hollow_wisp", _one, (cr_hollow_wisp,), (512, 512), 1.0),
    ("cr_hollow_spirit", None, (), (512, 1024), None),
    ("cr_hollow_treant", _one, (cr_hollow_treant,), (512, 1024), 2.6),
    ("cr_hollow_warden", _one, (cr_hollow_warden,), (1024, 1024), 4.5),
    ("cr_training_dummy", _one, (cr_training_dummy,), (512, 1024), 1.6),
    ("pet_wolf", _quad, ("pet_wolf",), (1024, 512), 1.0),
    ("pet_cat", _quad, ("pet_cat",), (1024, 512), 0.75),
    ("pet_boar", _quad, ("pet_boar",), (1024, 512), 0.9),
    ("pet_bear", _quad, ("pet_bear",), (1024, 512), 1.4),
    ("pet_owl", _one, (pet_owl,), (512, 512), 0.8),
    ("demon_imp", _one, (demon_imp,), (512, 512), 0.9),
    ("demon_voidwalker", _one, (demon_voidwalker,), (512, 1024), 2.2),
    ("demon_succubus", None, (), (512, 1024), None),
    ("demon_felhunter", _quad, ("felhunter",), (1024, 512), 1.2),
    ("demon_infernal", _one, (demon_infernal,), (1024, 1024), 2.6),
    ("totem_earth", _one, (lambda k: totem(k, "earth"),), (256, 512), 1.2),
    ("totem_fire", _one, (lambda k: totem(k, "fire"),), (256, 512), 1.2),
    ("totem_water", _one, (lambda k: totem(k, "water"),), (256, 512), 1.2),
    ("totem_air", _one, (lambda k: totem(k, "air"),), (256, 512), 1.2),
]

# ground line (design y, at 2x design canvas these are 1x units) for the pivot
BASE = {(1024, 512): 488, (512, 512): 490, (1024, 1024): 1000, (512, 1024): 990, (256, 512): 496}


def _humanoid(key, fn):
    return fn(key)


def register(reg):
    for key, fn, args, size, h in CREATURES:
        floating = key in ("cr_hollow_wisp",)
        if h is None:
            reg.spec(key, "Creature", size=size, height=C.CANVAS_M, pivot=(0.5, 0.05), shadow=True)
        else:
            base = BASE[size]
            if key in ("cr_training_dummy", "cr_hollow_treant", "demon_voidwalker"):
                base = 990
            piv = round(1.0 - base / float(size[1]), 4)
            if floating:
                piv = 0.12
            reg.spec(key, "Creature", size=size, measure=h, pivot=(0.5, piv), shadow=True)
        if key == "cr_hollow_spirit":
            reg.job([key], hollow_spirit, key)
        elif key == "demon_succubus":
            reg.job([key], succubus, key)
        elif fn is bandit:
            reg.job([key], bandit, key, args[0])
        elif fn is _quad:
            reg.job([key], _quad, key, args[0])
        else:
            reg.job([key], _one_fn, key, args[0])


def _one_fn(key, fn):
    return {key: fn(key)}
