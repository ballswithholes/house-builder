"""Environment art: seamless background layers, tiling ground textures, ground decals, foreground.

Backgrounds are painted on padded canvases with every object drawn at x-W, x, x+W and all noise
periodic over the image width, so the cropped result tiles seamlessly horizontally. Ground tiles do
the same in both axes.
"""
import math

import numpy as np

import palette as P
from brushes import (Canvas, F32, blob, bbox, blur, catmull, colfield, drop_shadow, ellipse, flat_fill,
                     glow, grain, outline, paint, ragged, rim_light, rng, smoothstep, stroke, taper,
                     to_image, warp, wash)

BG_PAD = 192


# ----------------------------------------------------------------------------------------
# shared helpers
# ----------------------------------------------------------------------------------------
def bg_canvas(key, w=2048, h=1024):
    return Canvas(w, h, 1.0, pad=BG_PAD, wrap_x=True, seed=key)


def tile_canvas(key, n=1024):
    return Canvas(n, n, 1.0, pad=160, wrap_x=True, wrap_y=True, seed=key)


def finish(cv, grain_amt=0.014, do_grade=True):
    grain(cv, grain_amt, cell=1.5, key="paper")
    rgb, a = cv.crop_pad()
    return to_image(rgb, a, bleed=True, do_grade=do_grade)


def merge(dst, src, opacity=1.0, soften=0.0):
    """Composite canvas src over dst (same geometry), optionally softening src edges."""
    rgb, a = src.rgb, src.a
    if soften > 0:
        bb = bbox(a, int(soften * 4) + 2)
        if bb is None:
            return
        ys, xs = bb
        rgb = rgb.copy()
        a = a.copy()
        rgb[ys, xs] = blur(rgb[ys, xs], soften)
        a[ys, xs] = blur(a[ys, xs], soften)
    k = opacity
    dst.rgb[:] = rgb * k + dst.rgb * (1.0 - a[..., None] * k)
    dst.a[:] = a * k + dst.a * (1.0 - a * k)


def layer_like(cv, tag):
    c = Canvas(cv.w, cv.h, cv.s, pad=max(cv.padx, cv.pady), wrap_x=cv.wrap_x, wrap_y=cv.wrap_y,
               seed=cv.seed + "/" + tag)
    return c


def col_field(cv, c):
    return np.broadcast_to(np.asarray(c, F32), (cv.PH, cv.PW, 3)).copy()


def blur1d(v, s):
    return blur(np.asarray(v, F32)[None, :], s, mode="edge")[0] if s > 0.3 else v


def slope_light(prof, smooth=6.0, k=2.0):
    dp = np.gradient(blur1d(prof, smooth))
    return np.clip(-dp * k, -1, 1)


# ----------------------------------------------------------------------------------------
# little painted motifs reused by several layers
# ----------------------------------------------------------------------------------------
def round_tree(cv, x, y, h, col, r, key, trunk=True, line=0.35, shade=1.0, shadow=True):
    """A fluffy round deciduous tree standing at (x, y) of total height h."""
    sub = cv.sub(x - h * 0.75, y - h * 1.25, x + h * 0.75, y + h * 0.3)
    if sub is None:
        return None
    cv = sub
    cw = h * 0.62
    if trunk:
        tw = max(1.5, h * 0.07)
        tr = stroke([(x, y), (x + h * 0.01, y - h * 0.25), (x - h * 0.01, y - h * 0.45)], tw, tw * 0.7)
        paint(cv, cv.mask(tr), P.mix(P.WOOD_DK, col, 0.25), line=0.4 * line, line_w=0.8, key=key + "t",
              ao=0, var=0.03, soft=1.5)
    cy = y - h * 0.62
    if shadow:
        sh = cv.ellipse_mask(x + h * 0.05, y, h * 0.32, h * 0.06)
        cv.atop(P.mix(P.INK, P.VIOLET, 0.4), sh * 0.25)
    m = cv.mask(blob(x, cy, cw * 0.5, h * 0.36, r, 0.14, 8))
    for i in range(3):
        a = r.random() * math.pi
        m = np.maximum(m, cv.mask(blob(x + math.cos(a) * cw * 0.28 * (1 if i % 2 else -1), cy - h * 0.08 + r.random() * h * 0.2,
                                       cw * (0.25 + r.random() * 0.1), h * (0.2 + r.random() * 0.08), r, 0.18, 7)))
    m = warp(cv, m, max(1.0, h * 0.03), max(4, h * 0.15), key + "wp")
    paint(cv, m, col, line=line, line_w=max(0.7, h * 0.012), key=key, ao=0.35, shade=shade, hi=0.4,
          var=0.08, var_cell=max(4, h * 0.2), cel=0.35)
    return m


def conifer_poly(x, base_y, h, w, r, tiers=None):
    tiers = tiers or max(4, int(h / max(w * 0.45, 6)))
    left, right = [], []
    for j in range(tiers + 1):
        t = j / tiers
        y = base_y - h + h * t
        hw = w * 0.5 * (0.15 + 0.85 * t) * (0.85 + 0.3 * r.random())
        droop = h / tiers * 0.35
        left.append((x - hw, y + droop))
        right.append((x + hw, y + droop))
        if j < tiers:
            yi = y + h / tiers * 0.65
            hwi = hw * 0.55
            left.append((x - hwi, yi))
            right.append((x + hwi, yi))
    pts = [(x + (r.random() - 0.5) * w * 0.05, base_y - h - h * 0.02)] + right + [(x + w * 0.08, base_y + 2), (x - w * 0.08, base_y + 2)] + left[::-1]
    return np.asarray(pts)


def smoke_plume(cv, x, y, h, r, key, alpha=0.35, color=None):
    pts = [(x, y)]
    cx = x
    for i in range(1, 6):
        cx += h * 0.08 + r.normal() * h * 0.05
        pts.append((cx, y - h * i / 5))
    s = stroke(pts, h * 0.06, h * 0.22, n=8)
    m = cv.mask(s)
    m = ragged(cv, m, 0.5, soft=h * 0.04, cell=h * 0.08, key=key)
    m = cv.blur(m, h * 0.02)
    grad = smoothstep(y - h, y, cv.yy())
    flat_fill(cv, m * (0.3 + 0.7 * np.broadcast_to(grad, m.shape)), color or P.WHITE_WARM, alpha)


# ----------------------------------------------------------------------------------------
# backgrounds
# ----------------------------------------------------------------------------------------
CLOUD_WHITE = P.hx("fdf7ec")
CLOUD_SHADE = P.hx("bfc2df")
CLOUD_SHADE_WARM = P.hx("d9c9d8")


def paint_cloud(cv, cx, by, w, h, r, key, warm=0.0):
    n = max(5, int(w / 50))
    puffs = []
    for i in range(n):
        t = (i + 0.5) / n
        bump = math.sin(math.pi * t) ** 0.8
        rad = h * (0.16 + 0.42 * bump) * (0.6 + 0.8 * r.random() ** 1.5)
        x = cx - w / 2 + t * w + r.normal() * w * 0.03
        y = by - rad * 0.55 - bump * h * 0.12
        puffs.append((x, y, rad))
    for i in range(max(2, n // 2)):
        t = 0.18 + 0.64 * r.random()
        bump = math.sin(math.pi * t)
        rad = h * (0.2 + 0.28 * bump) * (0.8 + 0.4 * r.random())
        x = cx - w / 2 + t * w
        y = by - h * 0.42 * bump - rad * 0.5 - h * 0.08
        puffs.append((x, y, rad))
    puffs.sort(key=lambda p: p[1])  # top (back) puffs first
    Y = cv.yy()
    base_clip = smoothstep(by + 3, by - 14, Y)
    union = np.zeros((cv.PH, cv.PW), F32)
    shade = P.mix(CLOUD_SHADE, CLOUD_SHADE_WARM, warm)
    for i, (x, y, rad) in enumerate(puffs):
        mf = cv.mask(blob(x, y, rad, rad * 0.92, r, 0.08, 9))
        m = mf * np.broadcast_to(base_clip, mf.shape)
        drop_shadow(cv, m, 2, 4, rad * 0.12, 0.10, color=shade)
        paint(cv, m, CLOUD_WHITE, nmask=mf, lo=shade, light_col=P.hx("fff6e2"), shade=0.75, hi=0.35, line=0.18,
              line_w=1.4, line_col=shade, soft=rad * 0.28, ao=0.0, var=0.04, var_cell=30, key=key + str(i),
              cel=0.6)
        union = np.maximum(union, m)
    bot = smoothstep(by - h * 0.55, by, Y)
    cv.atop(shade, np.clip(union * np.broadcast_to(bot, union.shape) * 0.5, 0, 1))
    return union


def bg_clouds(key="bg_clouds"):
    cv = bg_canvas(key)
    r = rng(key)
    lay = layer_like(cv, "wisps")
    # thin high wisps
    for i in range(6):
        x = r.random() * 2048
        y = 80 + r.random() * 180
        L = 260 + r.random() * 380
        for ox in cv.copies_x():
            s = stroke([(x + ox - L / 2, y + 6), (x + ox, y - 4), (x + ox + L / 2, y + 3)], 12 + r.random() * 10, 4,
                       profile=lambda t: math.sin(math.pi * min(1, max(0, t))) ** 0.6 + 0.05)
            m = ragged(lay, lay.mask(s), 0.7, soft=3, cell=12, key="wisp%d" % i)
            wash(lay, m, CLOUD_WHITE, 0.55, pool=0.2, gran=0.05, key="wi%d" % i)
    merge(cv, lay, 1.0, soften=1.2)
    # cumulus rows: (base y, list of (x, width, height))
    rows = [
        (330, [(150, 260, 110), (640, 200, 90), (1180, 330, 130), (1700, 240, 100)], 0.8, 0.5),
        (500, [(380, 520, 200), (1450, 600, 230), (1960, 260, 120)], 0.92, 0.25),
        (660, [(900, 760, 290), (40, 380, 170), (1640, 340, 150)], 1.0, 0.0),
    ]
    for ri, (by, clouds, op, warm) in enumerate(rows):
        lay = layer_like(cv, "row%d" % ri)
        for ci, (x, w, h) in enumerate(clouds):
            x = x + r.normal() * 30
            w = w * (0.9 + 0.2 * r.random())
            yb = by + r.normal() * 20
            seed = r.integers(1 << 30)
            for ox in cv.copies_x():
                if x + ox + w < -BG_PAD or x + ox - w > 2048 + BG_PAD:
                    continue
                paint_cloud(lay, x + ox, yb, w, h, np.random.default_rng(seed), "cl%d_%d" % (ri, ci), warm=warm)
        merge(cv, lay, op, soften=1.0)
    # gentle haze band low in the sky
    Y = cv.yy()
    haze = smoothstep(600, 760, Y) * (1 - smoothstep(780, 920, Y))
    hz = np.broadcast_to(haze, (cv.PH, cv.PW)) * (0.6 + 0.4 * cv.noise((40, 400), "haze", 3))
    wash(cv, hz.astype(F32), P.hx("f3ece6"), 0.25, pool=0, gran=0, var=0.02, key="hz")
    return finish(cv, 0.015)


def mountain_layer(cv, r, base, amp, col, key, n_peaks=6, snow=0.0, haze=0.0, fade=1.0, round_top=10.0):
    """Faceted watercolour mountains: lit left faces, shadowed right faces split along ridge spines."""
    W = cv.cw
    X = (np.arange(cv.PW, dtype=F32) - cv.padx)
    peaks = []
    for i in range(n_peaks):
        px = (i + 0.15 + r.random() * 0.7) * W / n_peaks
        ph = base - amp * (0.35 + 0.65 * r.random())
        sl = 0.42 + r.random() * 0.5
        sr = 0.42 + r.random() * 0.5
        peaks.append((px, ph, sl, sr, r.random() * 2 - 1, (r.random() - 0.5) * 0.5))
    # secondary shoulder peaks
    for i in range(n_peaks * 2):
        px = r.random() * W
        ph = base - amp * (0.1 + 0.35 * r.random())
        peaks.append((px, ph, 0.5 + r.random() * 0.4, 0.5 + r.random() * 0.4, r.random() * 2 - 1, (r.random() - 0.5) * 0.5))
    surfs = []
    meta = []
    for (px, ph, sl, sr, sp, bias) in peaks:
        for ox in (-W, 0, W):
            d = X - (px + ox)
            s = np.where(d < 0, sl, sr)
            dd = np.sqrt(d * d + round_top * round_top) - round_top
            surfs.append(ph + s * dd)
            meta.append((px + ox, ph, sp, bias))
    surfs = np.stack(surfs)
    owner = np.argmin(surfs, axis=0)
    prof = surfs[owner, np.arange(cv.PW)]
    biases = np.array([mm[3] for mm in meta], F32)
    jag = cv.periodic_noise(r, 26, octaves=3) - 0.5
    prof = prof + jag * amp * 0.07
    m = cv.profile_mask(prof, 1.0)
    m = ragged(cv, m, 0.2, soft=1.0, cell=4, key=key + "rg")
    Y = cv.yy()
    depth = Y - prof[None, :]
    # per-pixel owner: valleys slant because each peak's claim grows differently with depth
    vn = (cv.noise((120, 90), key + "vn", 2) - 0.5) * amp * 0.07
    best = None
    own2 = np.zeros((cv.PH, cv.PW), np.int32)
    for k in range(len(meta)):
        if not np.any(surfs[k] < 2000):
            continue
        sc = surfs[k][None, :] - biases[k] * np.clip(depth, 0, None) + (vn if k % 2 else -vn)
        if best is None:
            best = sc
            own2[:] = k
        else:
            better = sc < best
            best = np.where(better, sc, best)
            own2 = np.where(better, k, own2)
    opx = np.array([mm[0] for mm in meta], F32)[own2]
    oph = np.array([mm[1] for mm in meta], F32)[own2]
    osp = np.array([mm[2] for mm in meta], F32)[own2]
    # spine wanders down from the apex
    wob = (cv.noise((90, 40), key + "sp", 3) - 0.5) * 60
    spine = opx + (Y - oph) * 0.18 * osp + wob * np.clip((Y - oph) / 200, 0, 1)
    side = smoothstep(-3, 3, X[None, :] - spine)          # 0 lit face, 1 shadow face
    gully = (cv.noise((70, 9), key + "gu", 3) - 0.5) * 0.5
    c = np.asarray(col, F32)
    c_lit = np.asarray(P.light_of(col, 1.1), F32)
    c_sh = np.asarray(P.shadow_of(col, 0.85), F32)
    k = np.clip((1 - side) * 0.75 + gully * (1 - side), 0, 1)[..., None]
    field = c + (c_lit - c) * k
    field = field + (c_sh - field) * np.clip(side * 0.9 + gully * side, 0, 1)[..., None]
    t = smoothstep(0, amp * 2.0, depth) * fade
    field = field + (np.asarray(P.MIST, F32) - field) * np.clip(t * 0.8 + haze, 0, 1)[..., None]
    wash(cv, m, col, 1.0, field=field, pool=0.25, pool_w=2.5, gran=0.05, var=0.03, key=key)
    if snow > 0:
        thr = base - amp * 0.55
        high = np.clip((thr - oph) / (amp * 0.45), 0, 1)
        sd = high * amp * 0.45
        sn = cv.noise((26, 7), key + "sn", 3)
        smask = m * (depth < (sd * (0.55 + 0.9 * sn)) - 2) * (high > 0)
        smask = cv.blur(smask.astype(F32), 0.7)
        sf = colfield(P.hx("fbf6ff"), P.hx("bdb7d8"), np.clip(side, 0, 1))
        wash(cv, smask, P.WHITE_WARM, snow, field=sf, pool=0.25, gran=0.04, key=key + "snow")
    return prof


def bg_mountains_far(key="bg_mountains_far"):
    cv = bg_canvas(key)
    r = rng(key)
    Y = cv.yy()
    mountain_layer(cv, r, 560, 330, P.mix(P.VIOLET_FAR, P.MIST, 0.3), key + "a", n_peaks=5, snow=0.95, haze=0.1)
    band = np.exp(-((Y - 600) / 70) ** 2) * (0.5 + 0.7 * cv.noise((30, 300), "mist0", 3))
    wash(cv, np.clip(band, 0, 1).astype(F32) * (cv.a > 0.01), P.MIST, 0.4, pool=0, gran=0, var=0.0, key="m0")
    mountain_layer(cv, r, 700, 210, P.mix(P.DUSTY_BLUE, P.VIOLET_FAR, 0.5), key + "b", n_peaks=7, haze=0.05, round_top=16)
    band = np.exp(-((Y - 740) / 80) ** 2) * (0.5 + 0.7 * cv.noise((30, 300), "mist1", 3))
    wash(cv, np.clip(band, 0, 1).astype(F32) * (cv.a > 0.01), P.MIST, 0.45, pool=0, gran=0, var=0.0, key="m1")
    mountain_layer(cv, r, 830, 110, P.mix(P.DUSTY_BLUE, P.SAGE, 0.45), key + "c", n_peaks=7, haze=0.0, round_top=60)
    band = smoothstep(820, 1024, Y) * (0.8 + 0.3 * cv.noise((30, 300), "mist2", 3))
    wash(cv, np.clip(np.broadcast_to(band, (cv.PH, cv.PW)), 0, 1).astype(F32) * (cv.a > 0.01), P.MIST, 0.7, pool=0,
         gran=0, var=0.0, key="m2")
    return finish(cv)


def hill_band(cv, r, base, amp, col_top, col_bot, depth_px, key, harm=None, light=0.35, opacity=1.0, edge=0.3):
    harm = harm or [(1, amp * 0.35), (2, amp * 0.5), (3, amp * 0.3), (4, amp * 0.18), (6, amp * 0.08)]
    prof = base + cv.periodic(r, harm) + (cv.periodic_noise(r, 240, 3) - 0.5) * amp * 0.25
    m = cv.profile_mask(prof, 1.0)
    m = ragged(cv, m, 0.3, soft=1.2, cell=4, key=key + "rg")
    Y = cv.yy()
    depth = Y - prof[None, :]
    t = smoothstep(0, depth_px, depth)
    field = colfield(col_top, col_bot, t)
    lit = slope_light(prof, 10, 3.0)
    fall = np.exp(-np.clip(depth, 0, None) / (amp * 1.5 + 20))
    f = lit[None, :] * fall * light
    c_lit = np.asarray(P.light_of(col_top, 1.0), F32)
    c_sh = np.asarray(P.shadow_of(col_top, 0.8), F32)
    field = field + (c_lit - field) * np.clip(f, 0, 1)[..., None] + (c_sh - field) * np.clip(-f, 0, 1)[..., None]
    # sunlit crest
    crest = np.exp(-np.clip(depth, 0, None) / 10.0) * (depth > 0)
    field = field + (np.asarray(P.light_of(col_top, 1.3), F32) - field) * (crest * 0.35)[..., None]
    wash(cv, m, col_top, opacity, field=field, pool=edge, pool_w=3, gran=0.07, var=0.05, var_cell=90,
         bloom=0.2, key=key)
    return prof


def scatter_on(prof, r, n, depth_min, depth_max, W=2048, padx=BG_PAD):
    out = []
    for _ in range(n):
        x = r.random() * W
        d = depth_min + r.random() * (depth_max - depth_min)
        out.append((x, float(prof[int(x) + padx]) + d))
    return out


def field_patches(cv, prof, r, key, cols, n=10, depth=(20, 200), size=(80, 220), op=0.35):
    for i in range(n):
        x = r.random() * 2048
        d = depth[0] + r.random() * (depth[1] - depth[0])
        y = float(prof[int(x) + BG_PAD]) + d
        w = size[0] + r.random() * (size[1] - size[0])
        seed = r.integers(1 << 30)
        c = cols[r.integers(len(cols))]
        for ox in cv.copies_x():
            sub = cv.sub(x + ox - w, y - w * 0.4, x + ox + w, y + w * 0.4)
            if sub is None:
                continue
            rr = np.random.default_rng(seed)
            m = sub.mask(blob(x + ox, y, w * 0.8, w * 0.22, rr, 0.25, 8))
            m = ragged(sub, m, 0.5, 2, 8, key + "fp") * (sub.a > 0.5)
            wash(sub, m.astype(F32), c, op, pool=0.3, gran=0.12, key=key + "fpw")


def tree_cluster(cv, prof, r, x0, n, depth, hrange, col, key, spread=40, line=0.2, trunk=False):
    items = []
    for j in range(n):
        x = x0 + (r.random() - 0.5) * spread * n * 0.6
        y = float(prof[int(x) % 2048 + BG_PAD]) + depth + r.random() * spread * 0.6
        items.append((y, x, hrange[0] + r.random() * (hrange[1] - hrange[0]), r.integers(1 << 30)))
    for y, x, h, s in sorted(items):
        for ox in cv.copies_x():
            round_tree(cv, x + ox, y, h, col, np.random.default_rng(s), key, trunk=trunk, line=line, shade=0.75)


def bg_hills_far(key="bg_hills_far"):
    cv = bg_canvas(key)
    r = rng(key)
    bands = [
        (520, 80, P.atmos(P.SAGE, 0.55), P.atmos(P.SAGE, 0.75), 0.55),
        (610, 90, P.atmos(P.mix(P.SAGE, P.GRASS_LIGHT, 0.4), 0.38), P.atmos(P.SAGE, 0.6), 0.4),
        (720, 100, P.atmos(P.mix(P.GRASS_LIGHT, P.SAGE, 0.5), 0.22), P.atmos(P.MOSS, 0.45), 0.28),
    ]
    for bi, (base, amp, ct, cb, hz) in enumerate(bands):
        prof = hill_band(cv, r, base, amp, ct, cb, 420, key + "b%d" % bi)
        field_patches(cv, prof, r, key + str(bi), [P.atmos(P.LEAF_YELLOW, hz + 0.1), P.atmos(P.MOSS, hz + 0.1),
                                                   P.atmos(P.GRASS_LIGHT, hz)], n=8 + bi * 3, depth=(30, 160),
                      size=(60 + bi * 30, 160 + bi * 50), op=0.3)
        tree_col = P.atmos(P.MOSS_DK, hz + 0.1)
        for c in range(7 + bi * 3):
            x0 = r.random() * 2048
            tree_cluster(cv, prof, r, x0, 2 + r.integers(0, 5), 8 + r.random() * (40 + bi * 40),
                         ((10 + bi * 6), (18 + bi * 12)), tree_col, "t%d" % bi, spread=14 + bi * 8,
                         line=0.0 if bi < 2 else 0.15, trunk=bi == 2)
    Y = cv.yy()
    mist = smoothstep(780, 1024, Y)
    wash(cv, np.broadcast_to(mist, (cv.PH, cv.PW)).astype(F32) * (cv.a > 0.01), P.MIST, 0.55, pool=0, gran=0,
         var=0.02, key="mist")
    return finish(cv)


def bg_hills_near(key="bg_hills_near"):
    cv = bg_canvas(key)
    r = rng(key)
    p1 = hill_band(cv, r, 470, 90, P.mix(P.GRASS_LIGHT, P.SAGE, 0.35), P.mix(P.SAGE, P.MOSS, 0.5), 500, key + "a",
                   light=0.4)
    # small trees on the back hill
    for (x, y) in sorted(scatter_on(p1, r, 30, 8, 120), key=lambda p: p[1]):
        seed = r.integers(1 << 30)
        h = 26 + r.random() * 22
        for ox in cv.copies_x():
            round_tree(cv, x + ox, y, h, P.mix(P.MOSS, P.SAGE, 0.3), np.random.default_rng(seed), "bt", line=0.15)
    p2 = hill_band(cv, r, 640, 110, P.mix(P.GRASS_LIGHT, P.MOSS, 0.25), P.mix(P.MOSS, P.MOSS_DK, 0.5), 400, key + "b",
                   light=0.5)
    # tree clumps on the front hill
    for ci in range(7):
        x0 = (ci + r.random() * 0.7) * 2048 / 7
        n = 2 + r.integers(0, 4)
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            rr = np.random.default_rng(seed)
            items = []
            for j in range(n):
                x = x0 + (j - n / 2) * 34 + rr.normal() * 10
                y = float(p2[int(x) % 2048 + BG_PAD]) + 30 + rr.random() * 50
                h = 70 + rr.random() * 70
                items.append((y, x, h, rr.integers(1 << 30)))
            for y, x, h, s in sorted(items):
                col = P.mix(P.MOSS_DK, P.MOSS, rr.random())
                round_tree(cv, x + ox, y, h, col, np.random.default_rng(s), "ct%d" % ci, line=0.4)
    # bushes along the bottom
    for i in range(18):
        x = r.random() * 2048
        y = 960 + r.random() * 60
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            rr = np.random.default_rng(seed)
            m = cv.mask(blob(x + ox, y, 60 + rr.random() * 50, 35 + rr.random() * 25, rr, 0.2))
            m = warp(cv, m, 3, 14, "bw%d" % i)
            paint(cv, m, P.mix(P.MOSS_DK, P.FOREST, 0.3), line=0.3, key="bu%d" % i, ao=0.3, var=0.08)
    return finish(cv)


def crown_row(cv, r, base, th, conifer_ratio, spacing=0.55):
    """Silhouette profile of a row of tree crowns + per-column crown centre (for lit/shadow sides)."""
    W = cv.cw
    X = np.arange(cv.PW, dtype=F32) - cv.padx
    prof = np.full(cv.PW, base + 40.0, F32)
    centre = np.zeros(cv.PW, F32)
    x = 0.0
    shapes = []
    while x < W:
        con = r.random() < conifer_ratio
        if con:
            hw = th * (0.17 + r.random() * 0.08)
            hh = th * (0.6 + r.random() * 0.35)
            shapes.append((x, hw, hh, True, 0.0, x))
            x += hw * (0.9 + r.random() * 0.8)
        else:
            cw = th * (0.4 + r.random() * 0.35)
            hh = th * (0.45 + r.random() * 0.35)
            nb = 3 + r.integers(0, 3)
            for k in range(nb):
                t = (k + 0.5) / nb
                bx = x - cw / 2 + t * cw
                bump_h = hh * (0.7 + 0.3 * math.sin(math.pi * t)) * (0.9 + 0.2 * r.random())
                br = cw / nb * (0.75 + 0.35 * r.random())
                shapes.append((bx, br, bump_h, False, 0.0, x))
            x += cw * (0.65 + r.random() * 0.4)
    for (cx, hw, hh, con, _, owner_x) in shapes:
        for ox in (-W, 0, W):
            d = X - (cx + ox)
            ad = np.abs(d)
            inside = ad < hw * (1.0 if not con else 1.05)
            if not inside.any():
                continue
            top = base - hh
            if con:
                tier = hw * 0.32
                tri = np.abs(((ad / tier) % 1.0) - 0.5) * 2
                shape = top + (ad / hw) * hh * 0.9 - tri * hw * 0.28 * np.clip(ad / hw * 1.5, 0, 1)
            else:
                u = np.clip(ad / hw, 0, 1)
                shape = top + (1 - np.sqrt(1 - u * u)) * hw * 0.9
            shape = np.where(inside, shape, 1e9)
            better = shape < prof
            prof = np.where(better, shape, prof)
            centre = np.where(better, owner_x + ox if not con else cx + ox, centre)
    return prof, centre


def bg_forest_far(key="bg_forest_far"):
    cv = bg_canvas(key)
    r = rng(key)
    Y = cv.yy()
    X = cv.xx()
    rows = [
        (440, 170, P.atmos(P.mix(P.DUSTY_BLUE, P.VIOLET_FAR, 0.3), 0.32), 0.35),
        (560, 190, P.atmos(P.mix(P.DUSTY_BLUE, P.SAGE, 0.45), 0.28), 0.3),
        (690, 210, P.atmos(P.mix(P.TEAL, P.SAGE, 0.45), 0.22), 0.3),
        (830, 230, P.atmos(P.mix(P.FOREST, P.MOSS, 0.35), 0.12), 0.25),
    ]
    for ri, (base, th, col, cr) in enumerate(rows):
        prof, centre = crown_row(cv, r, base, th, cr)
        m = cv.profile_mask(prof, 1.0)
        m = ragged(cv, m, 0.3, soft=1.0, cell=3, key="fr%d" % ri)
        depth = Y - prof[None, :]
        sh = np.roll(np.roll(m, 9, 0), 7, 1)
        lit = cv.blur(np.clip(m - sh, 0, 1), 4.0) * 1.6
        shade = cv.blur(np.clip(m - np.roll(np.roll(m, -7, 0), -9, 1), 0, 1), 6.0)
        c = np.asarray(col, F32)
        field = c + (np.asarray(P.light_of(col, 1.0), F32) - c) * np.clip(lit, 0, 1)[..., None]
        field = field + (np.asarray(P.shadow_of(col, 0.7), F32) - field) * np.clip(shade * 0.8, 0, 1)[..., None]
        fade = smoothstep(0, 260, depth)
        field = field + (np.asarray(P.MIST, F32) - field) * (fade * 0.55)[..., None]
        wash(cv, m, col, 1.0, field=field, pool=0.35, pool_w=2.5, gran=0.06, var=0.04, var_cell=50, key="fw%d" % ri)
        # mist rising between rows
        band = smoothstep(base + 230, base + 30, Y) * smoothstep(base - 120, base + 40, Y)
        mist = np.broadcast_to(band, (cv.PH, cv.PW)) * (0.55 + 0.5 * cv.noise((50, 380), "mi%d" % ri, 3))
        wash(cv, np.clip(mist, 0, 1).astype(F32) * (cv.a > 0.01), P.MIST, 0.6 - ri * 0.1, pool=0, gran=0, var=0,
             key="mw%d" % ri)
    return finish(cv)


def bg_forest_near(key="bg_forest_near"):
    cv = bg_canvas(key)
    r = rng(key)
    Y = cv.yy()
    # deep background wall of foliage
    back = cv.profile_mask(260 + cv.periodic(r, [(3, 40), (7, 25), (13, 12)]), 1.0)
    back = ragged(cv, back, 0.4, soft=2, cell=12, key="bk")
    fld = colfield(P.mix(P.FOREST, P.TEAL, 0.3), P.FOREST_DK, np.broadcast_to(smoothstep(200, 900, Y), (cv.PH, cv.PW)))
    wash(cv, back, P.FOREST, 1.0, field=fld, pool=0.2, gran=0.1, key="bkw")
    # far trunks (pale, misty)
    for i in range(14):
        x = r.random() * 2048
        w = 18 + r.random() * 18
        for ox in cv.copies_x():
            s = stroke([(x + ox, 1030), (x + ox + 4, 600), (x + ox - 3, 290)], w * 1.25, w)
            m = cv.mask(s)
            wash(cv, m, P.mix(P.FOREST, P.DUSTY_BLUE, 0.35), 0.85, pool=0.3, gran=0.1, key="ft%d" % i)
    mist = np.broadcast_to(smoothstep(950, 500, Y) * 0.0 + smoothstep(420, 900, Y) * 0.5, (cv.PH, cv.PW))
    wash(cv, mist.astype(F32) * (cv.a > 0.01), P.mix(P.MIST, P.SAGE, 0.3), 0.55, pool=0, gran=0, var=0, key="m1")
    # near trunks
    trunks = []
    for i in range(7):
        x = (i + 0.2 + r.random() * 0.6) * 2048 / 7
        w = 50 + r.random() * 60
        trunks.append((x, w, r.integers(1 << 30)))
    for (x, w, s) in trunks:
        for ox in cv.copies_x():
            rr = np.random.default_rng(s)
            lean = rr.normal() * 20
            pts = [(x + ox - lean, 1040), (x + ox, 700), (x + ox + lean * 0.5, 420), (x + ox + lean, 250)]
            sp = stroke(pts, w * 1.15, w * 0.8, n=10)
            flare = np.array([(x + ox - lean - w * 1.3, 1030), (x + ox - lean - w * 0.45, 900), (x + ox - lean + w * 0.45, 900),
                              (x + ox - lean + w * 1.3, 1030)])
            m = cv.polys_mask([sp, catmull(flare, True, 8)])
            bark = cv.noise((90, 6), "bark", 3)
            drop_shadow(cv, m, 10, 0, 14, 0.35)
            paint(cv, m, P.mix(P.WOOD_DK, P.FOREST_DK, 0.35), tex=bark, line=0.5, line_w=1.6, key="tr", soft=w * 0.3,
                  shade=1.1, hi=0.25, var=0.06)
            # soft moss on the lit side, thicker toward the roots
            moss = m * smoothstep(x + ox + w * 0.15, x + ox - w * 0.45, cv.xx()) * \
                (0.35 + 0.65 * smoothstep(500, 1000, cv.yy())) * smoothstep(0.35, 0.65, cv.noise(40, "moss", 3))
            moss = ragged(cv, moss.astype(F32), 0.4, 1.5, 6, "msr")
            wash(cv, moss, P.mix(P.MOSS, P.SAGE, 0.3), 0.75, pool=0.3, gran=0.2, key="ms")
    # canopy
    for ci in range(15):
        x = (ci + r.random() * 0.8) * 2048 / 15
        y = 215 + r.random() * 70
        rad = 115 + r.random() * 70
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            sub = cv.sub(x + ox - rad * 2.2, y - rad * 1.6, x + ox + rad * 2.2, y + rad * 1.6)
            if sub is None:
                continue
            rr = np.random.default_rng(seed)
            parts = [blob(x + ox, y, rad, rad * 0.6, rr, 0.18, 10)]
            for k in range(5):
                a = rr.random() * 6.28
                parts.append(blob(x + ox + math.cos(a) * rad * 0.65, y + math.sin(a) * rad * 0.32 - 12,
                                  rad * (0.38 + rr.random() * 0.15), rad * (0.3 + rr.random() * 0.1), rr, 0.2, 8))
            m = sub.polys_mask(parts)
            m = warp(sub, m, 6, 26, "cw")
            m = ragged(sub, m, 0.45, soft=2.0, cell=6, key="cr")
            drop_shadow(sub, m, 6, 16, 18, 0.35)
            col = P.mix(P.FOREST, P.MOSS_DK, 0.3 + rr.random() * 0.5)
            paint(sub, m, col, line=0.3, line_w=1.5, key="cp%d" % ci, ao=0.55, hi=0.45, shade=1.1, var=0.08,
                  var_cell=30, cel=0.35)
            # leaf clumps catching the light (upper-left of the crown)
            clumps = []
            for k in range(9):
                a = math.pi * (1.0 + 0.55 * rr.random())
                d = rad * (0.25 + 0.6 * rr.random())
                cx2, cy2 = x + ox + math.cos(a) * d * 1.1, y + math.sin(a) * d * 0.55
                clumps.append(blob(cx2, cy2, rad * (0.16 + 0.1 * rr.random()), rad * (0.1 + 0.06 * rr.random()), rr, 0.25, 7))
            cm = sub.polys_mask(clumps) * m
            paint(sub, cm.astype(F32), P.mix(col, P.GRASS_LIGHT, 0.45), line=0.35, line_w=1.0, key="lc%d" % ci, soft=6,
                  ao=0.2, hi=0.5, var=0.06, cel=0.5)
    # undergrowth
    for i in range(26):
        x = r.random() * 2048
        y = 1000 + r.random() * 40
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            rr = np.random.default_rng(seed)
            if i % 3 == 0:
                fern(cv, x + ox, y, 90 + rr.random() * 60, rr, "uf%d" % i, P.mix(P.MOSS_DK, P.FOREST, 0.4))
            else:
                m = cv.mask(blob(x + ox, y - 10, 70 + rr.random() * 60, 45 + rr.random() * 30, rr, 0.22))
                m = ragged(cv, warp(cv, m, 4, 16, "ub"), 0.5, 1.5, 5, "ubr")
                paint(cv, m, P.mix(P.FOREST_DK, P.MOSS_DK, rr.random()), line=0.3, key="ub%d" % i, ao=0.4, var=0.1)
    # light shafts
    for i in range(5):
        x = r.random() * 2048
        for ox in cv.copies_x():
            poly = np.array([(x + ox, 120), (x + ox + 60, 120), (x + ox - 240, 1024), (x + ox - 420, 1024)])
            m = cv.blur(cv.mask(poly), 12)
            fade = np.broadcast_to(smoothstep(1000, 200, Y), m.shape)
            cv.atop(P.hx("fff2c8"), (m * fade * 0.22).astype(F32))
    return finish(cv)


def fern(cv, x, y, h, r, key, col):
    """Arching fern: curved fronds with alternating tapered pinnae."""
    fronds = []
    nf = 7
    order = sorted(range(nf), key=lambda k: abs(k - (nf - 1) / 2), reverse=True)
    for k in order:
        side = (k - (nf - 1) / 2) / ((nf - 1) / 2)          # -1..1
        L = h * (1.05 - 0.35 * abs(side)) * (0.85 + 0.25 * r.random())
        a0 = -math.pi / 2 + side * 0.55
        curl = side * 0.9 + r.normal() * 0.1
        pts = []
        for j in range(9):
            t = j / 8
            a = a0 + curl * t * t
            if j == 0:
                px, py = x + side * h * 0.03, y
            else:
                px, py = pts[-1][0] + math.cos(a) * L / 8, pts[-1][1] + math.sin(a) * L / 8
            pts.append((px, py))
        pts = np.asarray(pts)
        polys = [stroke(pts, h * 0.025, h * 0.006, n=6)]
        dense = catmull(pts, closed=False, n=6)
        seg = np.diff(dense, axis=0)
        n = len(seg)
        npin = 15
        for j in range(1, npin):
            t = j / npin
            i = min(n - 1, int(t * n))
            p = dense[i]
            tx, ty = seg[i] / max(1e-6, float(np.hypot(*seg[i])))
            pl = h * 0.2 * (1 - t) ** 0.8 * (0.3 + 0.7 * min(1, t * 4)) + h * 0.015
            for s in (-1, 1):
                ang = math.atan2(ty, tx) + s * 1.05
                off = (j % 2) * 0.5 * s
                polys.append(leaf_poly(p[0] + tx * off, p[1] + ty * off, pl, ang, 0.32))
        fronds.append((polys, 0.85 + 0.25 * (1 - abs(side))))
    m_all = np.zeros((cv.PH, cv.PW), F32)
    for i, (polys, tone) in enumerate(fronds):
        m = cv.polys_mask(polys)
        drop_shadow(cv, m, 3, 5, 5, 0.25)
        paint(cv, m, P.scale_v(col, tone), line=0.6, line_w=1.0, key=key + str(i), ao=0.3, var=0.06, soft=4, hi=0.45,
              light_col=P.mix(P.GRASS_LIGHT, P.CREAM, 0.2))
        m_all = np.maximum(m_all, m)
    return m_all


def cliff_profile(cv, r, base, n_seg, h_range):
    W = cv.cw
    xs = np.sort(r.random(n_seg)) * W
    hs = base - r.random(n_seg) * h_range
    prof = np.zeros(W, F32)
    for i in range(n_seg):
        x0 = xs[i]
        x1 = xs[(i + 1) % n_seg] + (W if i == n_seg - 1 else 0)
        idx = (np.arange(int(x0), int(x1)) % W)
        prof[idx] = hs[i]
    prof = blur(np.tile(prof, 3)[None, :], 5, mode="edge")[0][W:2 * W]
    from brushes import periodic_noise_1d
    prof += (periodic_noise_1d(W, r, 90, 4) - 0.5) * 30 + (periodic_noise_1d(W, r, 12, 2) - 0.5) * 8
    return prof[(np.arange(cv.PW) - cv.padx) % W]


def cliff_layer(cv, r, base, h_range, col, key, n_seg=7, moss=1.0, haze=0.0, fade_depth=520):
    """Mossy cliff band: faceted vertical rock columns, ledges, moss caps with drips."""
    W = cv.cw
    prof = cliff_profile(cv, r, base, n_seg, h_range)
    m = cv.profile_mask(prof, 1.0)
    m = ragged(cv, m, 0.2, 1.0, 4, key + "rg")
    Y = cv.yy()
    X = (np.arange(cv.PW) - cv.padx) % W
    depth = Y - prof[None, :]
    # rock columns: random widths, each with its own facet orientation
    edges = [0.0]
    while edges[-1] < W:
        edges.append(edges[-1] + 26 + r.random() * 70)
    edges[-1] = W
    edges = np.array(edges)
    cid = np.searchsorted(edges, X, side="right") - 1
    orient = (r.random(len(edges)) * 2 - 1).astype(F32)
    o = orient[cid][None, :] + (cv.noise((140, 50), key + "or", 2) - 0.5) * 1.2
    # within a column: left part lit, right part shadow (prism)
    rel = ((X - edges[cid]) / np.maximum(1, edges[cid + 1] - edges[cid]))[None, :]
    prism = smoothstep(0.45, 0.65, rel + (cv.noise((60, 12), key + "pr", 2) - 0.5) * 0.5)
    f = np.clip(o * 0.6 + (0.5 - prism) * 0.9, -1, 1)
    c = np.asarray(col, F32)
    field = c + (np.asarray(P.light_of(col, 1.1), F32) - c) * np.clip(f, 0, 1)[..., None] \
        + (np.asarray(P.shadow_of(col, 1.0), F32) - c) * np.clip(-f, 0, 1)[..., None]
    # crack lines between columns
    dcol = np.minimum(X - edges[cid], edges[cid + 1] - X)[None, :]
    crack = smoothstep(2.5, 0.5, dcol + (cv.noise((30, 6), key + "cn", 2) - 0.5) * 3)
    field = field * (1 - 0.35 * crack)[..., None]
    # horizontal ledges
    led = np.abs(np.sin((Y + (cv.noise((40, 220), key + "lw", 2) - 0.5) * 50) / 70.0 * math.pi))
    ledge = smoothstep(0.1, 0.0, led) * (depth > 30)
    field = field + (np.asarray(P.light_of(col, 1.3), F32) - field) * (ledge * 0.35)[..., None]
    fade = smoothstep(0, fade_depth, depth)
    field = field + (np.asarray(P.MIST, F32) - field) * np.clip(fade * 0.7 + haze, 0, 1)[..., None]
    wash(cv, m, col, 1.0, field=field, pool=0.3, pool_w=2.5, gran=0.08, var=0.04, key=key)
    if moss > 0:
        drip = cv.noise((60, 7), key + "dr", 3)
        md = 12 + 70 * smoothstep(0.55, 0.9, drip) + 10 * cv.noise(6, key + "mn", 2)
        mm = m * (depth < md)
        # moss on ledges too
        mm = np.maximum(mm, m * ledge * smoothstep(0.5, 0.6, cv.noise(30, key + "lm", 3)))
        mm = ragged(cv, mm.astype(F32), 0.4, 1.0, 4, key + "mr")
        mf = colfield(P.mix(P.GRASS_LIGHT, P.MOSS, 0.35), P.MOSS_DK, np.clip(depth / 60, 0, 1))
        mf = mf + (np.asarray(P.MIST, F32) - mf) * np.clip(haze + fade[..., None][..., 0][..., None] * 0.5, 0, 1)
        wash(cv, mm, P.MOSS, moss, field=mf, pool=0.35, gran=0.15, key=key + "mo")
    return prof


def waterfall(cv, x, y0, y1, w, key):
    s = np.array([(x - w / 2, y0), (x + w / 2, y0), (x + w * 0.7, y1), (x - w * 0.7, y1)])
    m = cv.mask(s)
    streak = cv.noise((90, 3), key + "s", 3)
    fld = colfield(P.hx("dfe9f2"), P.WHITE_WARM, smoothstep(0.4, 0.7, streak))
    wash(cv, m, P.WHITE_WARM, 0.85, field=fld, pool=0.2, gran=0.05, key=key)
    foam = cv.blur(cv.ellipse_mask(x, y1, w * 2.2, w * 0.7), 8)
    flat_fill(cv, foam, P.WHITE_WARM, 0.6)


def bg_shrine_cliffs(key="bg_shrine_cliffs"):
    cv = bg_canvas(key)
    r = rng(key)
    Y = cv.yy()
    # far misty cliffs
    cliff_layer(cv, r, 360, 160, P.atmos(P.mix(P.STONE, P.VIOLET_FAR, 0.6), 0.35), key + "far", n_seg=9, moss=0.7,
                haze=0.35, fade_depth=300)
    for ox in cv.copies_x():
        for ti in range(6):
            tx = (ti * 351 + 90) % 2048 + ox
            ty = 360 - 150 * ((ti * 37) % 10) / 10.0
    mist = np.broadcast_to(smoothstep(330, 620, Y), (cv.PH, cv.PW)) * (0.7 + 0.3 * cv.noise((40, 300), "m0", 3))
    wash(cv, mist.astype(F32) * (cv.a > 0.01), P.MIST, 0.65, pool=0, gran=0, var=0, key="m0")
    # mid cliffs with stairs, shrine and waterfall
    prof = cliff_layer(cv, r, 520, 230, P.mix(P.STONE_WARM, P.VIOLET, 0.22), key + "main", n_seg=6, moss=1.0, fade_depth=520)
    xi = int(np.argmin(prof[cv.padx:cv.padx + cv.cw]))
    top_y = float(prof[xi + cv.padx])
    wx = (xi + 900) % 2048
    wy = float(prof[int(wx) + cv.padx]) + 4
    for ox in cv.copies_x():
        waterfall(cv, wx + ox, wy, 860, 26, "wf")
    sx = (xi - 270) % 2048
    steps = 16
    step_h = (860 - top_y) / steps
    for ox in cv.copies_x():
        polys_t, polys_r = [], []
        for i in range(steps):
            x = sx + ox + i * 16
            y = 860 - i * step_h
            polys_r.append(np.array([(x, y - step_h), (x + 66, y - step_h), (x + 66, y), (x, y)]))
            polys_t.append(np.array([(x, y - step_h - 5), (x + 66, y - step_h - 5), (x + 66, y - step_h + 1), (x, y - step_h + 1)]))
        mr = cv.polys_mask(polys_r)
        drop_shadow(cv, mr, 8, 4, 6, 0.4)
        paint(cv, mr, P.atmos(P.STONE_WARM, 0.1), line=0.6, line_w=1.0, key="st", soft=4, shade=0.6, ao=0)
        mt = cv.polys_mask(polys_t)
        paint(cv, mt, P.light_of(P.STONE_WARM, 1.2), line=0.4, line_w=0.8, key="stt", soft=2, shade=0.3, ao=0)
        mo = np.clip(mr + mt, 0, 1) * smoothstep(0.6, 0.72, cv.noise(9, "smo", 3))
        wash(cv, mo.astype(F32), P.MOSS, 0.85, pool=0.3, gran=0.2, key="smo")
    plateau = [(xi + 110) % 2048, (xi + 640) % 2048, (xi + 1250) % 2048, (xi + 1650) % 2048]
    for pi, px in enumerate(plateau):
        py = float(prof[int(px) + cv.padx]) + 6
        for ox in cv.copies_x():
            x = px + ox
            if pi == 0:
                torii(cv, x, py, 150, "to")
            elif pi == 2:
                torii(cv, x, py, 90, "to2", faded=0.4)
            else:
                for k in range(2):
                    hh = 50 + 45 * ((pi + k) % 2)
                    pl = np.array([(x + k * 70 - 14, py), (x + k * 70 - 13, py - hh), (x + k * 70 - 2, py - hh - 12),
                                   (x + k * 70 + 6, py - hh + 4), (x + k * 70 + 14, py - hh), (x + k * 70 + 14, py)])
                    paint(cv, cv.mask(pl), P.STONE_WARM, line=0.6, line_w=1.0, key="ru", soft=4)
    for i in range(16):
        x = r.random() * 2048
        y = float(prof[int(x) + cv.padx]) + 8
        h = 60 + r.random() * 90
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            rr = np.random.default_rng(seed)
            if i % 3 == 0:
                sub = cv.sub(x + ox - h, y - h * 1.2, x + ox + h, y + 10)
                if sub is not None:
                    m = sub.mask(conifer_poly(x + ox, y, h, h * 0.42, rr))
                    paint(sub, m, P.mix(P.FOREST, P.MOSS_DK, 0.5), line=0.35, key="cp", soft=5, ao=0.3)
            else:
                round_tree(cv, x + ox, y, h * 0.85, P.mix(P.MOSS_DK, P.MOSS, rr.random() * 0.6), rr, "rt", line=0.3)
    for i in range(40):
        x = r.random() * 2048
        y = float(prof[int(x) + cv.padx]) + 6
        L = 30 + r.random() * 140
        for ox in cv.copies_x():
            s = stroke([(x + ox, y), (x + ox + 3, y + L * 0.5), (x + ox - 2, y + L)], 3.5, 1.0, n=6)
            wash(cv, cv.mask(s), P.MOSS_DK, 0.85, pool=0, gran=0.1, key="vn")
    mist = np.broadcast_to(smoothstep(700, 1000, Y), (cv.PH, cv.PW)) * (0.75 + 0.3 * cv.noise((40, 300), "m2n", 3))
    wash(cv, np.clip(mist, 0, 1).astype(F32) * (cv.a > 0.01), P.MIST, 0.75, pool=0, gran=0, var=0, key="m2")
    # near low ledge with bushes
    p3 = hill_band(cv, r, 930, 30, P.mix(P.MOSS, P.SAGE, 0.4), P.MOSS_DK, 120, key + "near",
                   harm=[(2, 12), (3, 10), (7, 6)])
    for c in range(8):
        x0 = r.random() * 2048
        tree_cluster(cv, p3, r, x0, 2 + r.integers(0, 4), 18, (60, 110), P.mix(P.MOSS_DK, P.FOREST, 0.35), "nb",
                     spread=40, line=0.35, trunk=True)
    return finish(cv)


def torii(cv, x, y, h, key, col=P.VERMILION, faded=0.25, line=0.6, lw=1.2):
    col = P.atmos(col, faded) if faded else col
    w = h * 1.05
    pw = h * 0.085
    polys = []
    for sx in (-1, 1):
        px = x + sx * w * 0.33
        polys.append(np.array([(px - pw / 2, y), (px - pw * 0.42, y - h * 0.86), (px + pw * 0.42, y - h * 0.86), (px + pw / 2, y)]))
    m_post = cv.polys_mask(polys)
    paint(cv, m_post, col, line=line, line_w=lw, key=key + "p", soft=pw * 0.5)
    nuki = np.array([(x - w * 0.45, y - h * 0.66), (x + w * 0.45, y - h * 0.66), (x + w * 0.45, y - h * 0.6), (x - w * 0.45, y - h * 0.6)])
    paint(cv, cv.mask(nuki), col, line=line, line_w=lw, key=key + "n", soft=3)
    kasagi = catmull(np.array([(x - w * 0.6, y - h * 0.93), (x - w * 0.3, y - h * 0.86), (x, y - h * 0.86), (x + w * 0.3, y - h * 0.86),
                               (x + w * 0.6, y - h * 0.93), (x + w * 0.58, y - h * 1.0), (x, y - h * 0.95), (x - w * 0.58, y - h * 1.0)]), True, 6)
    paint(cv, cv.mask(kasagi), P.mix(col, P.INK, 0.55), line=line, line_w=lw, key=key + "k", soft=4)
    shimaki = np.array([(x - w * 0.47, y - h * 0.86), (x + w * 0.47, y - h * 0.86), (x + w * 0.47, y - h * 0.8), (x - w * 0.47, y - h * 0.8)])
    paint(cv, cv.mask(shimaki), col, line=line, line_w=lw, key=key + "s", soft=3)


def house_far(cv, x, y, w, h, roof, r, key, haze=0.3):
    wall = P.atmos(P.PLASTER, haze)
    rc = P.atmos(roof, haze)
    body = np.array([(x - w / 2, y), (x - w / 2, y - h), (x + w / 2, y - h), (x + w / 2, y)])
    paint(cv, cv.mask(body), wall, line=0.5, line_w=1.0, key=key + "w", soft=4, shade=0.6)
    side = np.array([(x + w / 2, y), (x + w / 2, y - h), (x + w / 2 + w * 0.25, y - h - w * 0.12), (x + w / 2 + w * 0.25, y - w * 0.12)])
    paint(cv, cv.mask(side), P.shadow_of(wall, 0.9), line=0.5, line_w=1.0, key=key + "sd", soft=3, shade=0.2)
    rh = h * (0.7 + r.random() * 0.4)
    roofp = np.array([(x - w / 2 - w * 0.1, y - h + 2), (x, y - h - rh), (x + w / 2 + w * 0.1, y - h + 2)])
    roofs = np.array([(x, y - h - rh), (x + w * 0.25, y - h - rh - w * 0.12), (x + w / 2 + w * 0.35, y - h - w * 0.12 + 2),
                      (x + w / 2 + w * 0.1, y - h + 2)])
    paint(cv, cv.mask(roofs), P.shadow_of(rc, 0.6), line=0.5, line_w=1.0, key=key + "rs", soft=3)
    paint(cv, cv.mask(roofp), rc, line=0.5, line_w=1.0, key=key + "r", soft=4)
    # glowing windows
    for k in range(1 + int(w > 40)):
        wx = x - w * 0.22 + k * w * 0.4
        wy = y - h * 0.55
        win = cv.mask(np.array([(wx - w * 0.07, wy - h * 0.16), (wx + w * 0.07, wy - h * 0.16), (wx + w * 0.07, wy + h * 0.1),
                                (wx - w * 0.07, wy + h * 0.1)]))
        flat_fill(cv, win, P.LANTERN, 0.95)
    return (x + w * 0.2, y - h - rh * 0.6)


def windmill_far(cv, x, y, h, key, haze=0.3):
    wall = P.atmos(P.PLASTER, haze)
    tower = np.array([(x - h * 0.16, y), (x - h * 0.1, y - h * 0.62), (x + h * 0.1, y - h * 0.62), (x + h * 0.16, y)])
    paint(cv, cv.mask(tower), wall, line=0.5, line_w=1.0, key=key + "t", soft=5)
    cap = cv.mask(ellipse(x, y - h * 0.62, h * 0.13, h * 0.09, 0, 24, math.pi, 2 * math.pi))
    paint(cv, cap, P.atmos(P.TERRACOTTA, haze), line=0.5, line_w=1.0, key=key + "c", soft=3)
    hub = (x, y - h * 0.62)
    blades = []
    for k in range(4):
        a = k * math.pi / 2 + 0.35
        ex, ey = hub[0] + math.cos(a) * h * 0.42, hub[1] + math.sin(a) * h * 0.42
        nx, ny = -math.sin(a), math.cos(a)
        blades.append(np.array([(hub[0], hub[1]), (ex, ey), (ex + nx * h * 0.07, ey + ny * h * 0.07),
                                (hub[0] + math.cos(a) * h * 0.1 + nx * h * 0.07, hub[1] + math.sin(a) * h * 0.1 + ny * h * 0.07)]))
    paint(cv, cv.polys_mask(blades), P.atmos(P.WOOD_LIGHT, haze), line=0.6, line_w=1.0, key=key + "b", soft=2)


def bg_village_far(key="bg_village_far"):
    cv = bg_canvas(key, 2048, 512)
    r = rng(key)
    Y = cv.yy()
    prof = hill_band(cv, r, 300, 70, P.atmos(P.mix(P.GRASS_LIGHT, P.SAGE, 0.5), 0.32), P.atmos(P.SAGE, 0.5), 200, key + "h",
                     harm=[(1, 30), (2, 26), (3, 12), (5, 5)])
    roofs = [P.TERRACOTTA, P.ROOF_BLUE, P.THATCH, P.mix(P.ROSE, P.TERRACOTTA, 0.5), P.TERRACOTTA_DK, P.ROOF_BLUE]
    # one village cluster + a few farms per tile
    groups = [(560, 16, 260), (1350, 4, 120), (1800, 3, 90)]
    houses = []
    for gx, n, spread in groups:
        for i in range(n):
            x = gx + (r.random() - 0.5) * spread * 2
            row = r.random()
            houses.append((x, row, r.integers(1 << 30), r.random(), r.random()))
    mill_x = 760
    houses.append((mill_x, 0.1, r.integers(1 << 30), -1, 0))
    houses.sort(key=lambda h: h[1])
    chimneys = []
    for (x, row, s, a, b) in houses:
        y = float(prof[int(x) % 2048 + cv.padx]) + 14 + row * 70
        for ox in cv.copies_x():
            rr = np.random.default_rng(s)
            if a < 0:
                windmill_far(cv, x + ox, y, 150, "wm")
                continue
            w = 34 + a * 30 + row * 10
            h = 24 + b * 16 + row * 6
            c = house_far(cv, x + ox, y, w, h, roofs[int(b * 6) % 6], rr, "hs", haze=0.3 - row * 0.1)
            if ox == 0 and rr.random() < 0.45:
                chimneys.append(c)
    # trees around the village
    for i in range(40):
        x = r.random() * 2048
        y = float(prof[int(x) + cv.padx]) + 10 + r.random() * 120
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            round_tree(cv, x + ox, y, 30 + r.random() * 26, P.atmos(P.mix(P.MOSS, P.MOSS_DK, r.random()), 0.3),
                       np.random.default_rng(seed), "vt", line=0.2, trunk=True)
    for i, (cx, cy) in enumerate(chimneys):
        seed = r.integers(1 << 30)
        for ox in cv.copies_x():
            smoke_plume(cv, cx + ox, cy, 120, np.random.default_rng(seed), "sm%d" % i, 0.4)
    p2 = hill_band(cv, r, 450, 22, P.atmos(P.GRASS_LIGHT, 0.2), P.atmos(P.MOSS, 0.35), 120, key + "f",
                   harm=[(1, 10), (2, 10), (4, 6)])
    mist = np.broadcast_to(smoothstep(440, 512, Y), (cv.PH, cv.PW))
    wash(cv, mist.astype(F32) * (cv.a > 0.01), P.MIST, 0.35, pool=0, gran=0, var=0, key="mv")
    return finish(cv)


# ----------------------------------------------------------------------------------------
# ground tiles (tile in both axes)
# ----------------------------------------------------------------------------------------
def scatter_xy(r, n, W=1024):
    return [(r.random() * W, r.random() * W, r.integers(1 << 30)) for _ in range(n)]


def each_copy(cv, x, y, margin):
    for ox in cv.copies_x():
        for oy in cv.copies_y():
            X, Y = x + ox, y + oy
            if -margin - 160 < X < cv.w + margin + 160 and -margin - 160 < Y < cv.h + margin + 160:
                yield X, Y


def grass_base(cv, c0, c1, key, dark=None):
    field = colfield(c0, c1, smoothstep(0.3, 0.7, cv.noise(260, key + "b", 4)))
    n2 = cv.noise(60, key + "c", 4)
    field = field * (0.9 + 0.2 * n2)[..., None]
    if dark is not None:
        dk = smoothstep(0.55, 0.75, cv.noise(120, key + "d", 3))
        field = field + (np.asarray(dark, F32) - field) * (dk * 0.5)[..., None]
    full = np.ones((cv.PH, cv.PW), F32)
    wash(cv, full, c0, 1.0, field=field, pool=0, gran=0.06, var=0.04, var_cell=30, key=key)


def blades(cv, r, n, length, cols, key, width=3.0, alpha=0.9, lean=0.25, cluster=None):
    """Many short grass blade strokes painted in a few colour groups."""
    groups = [[] for _ in cols]
    for i in range(n):
        if cluster is not None:
            cx, cy, rad = cluster[r.integers(len(cluster))]
            x = cx + r.normal() * rad
            y = cy + r.normal() * rad * 0.7
        else:
            x, y = r.random() * cv.w, r.random() * cv.h
        L = length * (0.6 + 0.8 * r.random())
        a = -math.pi / 2 + r.normal() * lean
        bend = r.normal() * 0.3
        g = r.integers(len(cols))
        for X, Y in each_copy(cv, x, y, L):
            pts = [(X, Y), (X + math.cos(a) * L * 0.5, Y + math.sin(a) * L * 0.5),
                   (X + math.cos(a + bend) * L, Y + math.sin(a + bend) * L)]
            groups[g].append(stroke(pts, width, 0.3, n=4, cap=False))
    for g, polys in enumerate(groups):
        if polys:
            m = cv.polys_mask(polys)
            wash(cv, m, cols[g], alpha, pool=0.15, pool_w=1, gran=0.05, var=0.06, var_cell=40, key=key + str(g))


def tiny_flower(cv, x, y, rad, petal, center, key, n=5):
    polys = []
    for k in range(n):
        a = k * 2 * math.pi / n
        polys.append(ellipse(x + math.cos(a) * rad * 0.55, y + math.sin(a) * rad * 0.45, rad * 0.45, rad * 0.32, a, 12))
    m = cv.polys_mask(polys)
    paint(cv, m, petal, line=0.4, line_w=0.6, soft=1.0, key=key, ao=0, var=0.0, hi=0.2)
    paint(cv, cv.ellipse_mask(x, y, rad * 0.25, rad * 0.22), center, line=0.0, soft=0.8, key=key + "c", ao=0)


def flower_patches(cv, r, n_patches, per, cols, rad=(7, 11), spread=45, key="fl"):
    for p in range(n_patches):
        cx, cy = r.random() * cv.w, r.random() * cv.h
        pc, cc = cols[r.integers(len(cols))]
        for i in range(per):
            x = cx + r.normal() * spread
            y = cy + r.normal() * spread * 0.7
            rr = rad[0] + r.random() * (rad[1] - rad[0])
            for X, Y in each_copy(cv, x, y, rr * 2):
                sub = cv.sub(X - rr * 2, Y - rr * 2, X + rr * 2, Y + rr * 2)
                if sub is not None:
                    tiny_flower(sub, X, Y, rr, pc, cc, key + "%d" % p)


def ground_meadow(key="ground_meadow"):
    cv = tile_canvas(key)
    r = rng(key)
    grass_base(cv, P.mix(P.GRASS_LIGHT, P.SAGE, 0.45), P.mix(P.SAGE, P.MOSS, 0.6), key, dark=P.MOSS)
    # sunlit and shaded swathes with watercolour edges
    for i, (c, op, th) in enumerate(((P.GRASS_LIGHT, 0.35, 0.62), (P.MOSS, 0.3, 0.64))):
        sw = smoothstep(th, th + 0.03, cv.noise(200, "sw%d" % i, 4))
        sw = ragged(cv, sw.astype(F32), 0.4, 2, 10, "swr%d" % i)
        wash(cv, sw, c, op, pool=0.5, pool_w=4, gran=0.1, key="sww%d" % i)
    # clover patches
    for i in range(26):
        x, y = r.random() * 1024, r.random() * 1024
        polys = []
        for k in range(int(6 + r.random() * 10)):
            px, py = x + r.normal() * 30, y + r.normal() * 22
            for X, Y in each_copy(cv, px, py, 14):
                for j in range(3):
                    a = j * 2.09 + r.random()
                    polys.append(ellipse(X + math.cos(a) * 4.5, Y + math.sin(a) * 3.6, 4.8, 3.9, a, 10))
        if polys:
            m = cv.polys_mask(polys)
            paint(cv, m, P.mix(P.MOSS, P.GRASS_LIGHT, 0.35), line=0.5, line_w=0.8, soft=2, key="cl", ao=0, var=0.05)
    blades(cv, r, 2200, 30, [P.mix(P.MOSS, P.MOSS_DK, 0.5), P.MOSS, P.mix(P.MOSS, P.GRASS_LIGHT, 0.5), P.GRASS_LIGHT],
           key + "bl", width=4.0, alpha=0.85,
           cluster=[(r.random() * 1024, r.random() * 1024, 40) for _ in range(90)])
    blades(cv, r, 900, 22, [P.MOSS, P.GRASS_LIGHT], key + "bl2", width=3.4, alpha=0.7)
    cols = [(P.WHITE_WARM, P.HONEY), (P.LEAF_YELLOW, P.AMBER), (P.ROSE, P.CREAM_WARM), (P.LAVENDER, P.CREAM)]
    flower_patches(cv, r, 16, 9, cols, (7, 11), 40)
    return finish(cv, 0.013)


def leaf_poly(x, y, L, a, w=0.45):
    pts = [(0, 0), (L * 0.3, -L * w * 0.5), (L * 0.7, -L * w * 0.45), (L, 0), (L * 0.7, L * w * 0.45), (L * 0.3, L * w * 0.5)]
    p = np.asarray(pts)
    c, s = math.cos(a), math.sin(a)
    return catmull(np.stack([x + p[:, 0] * c - p[:, 1] * s, y + p[:, 0] * s + p[:, 1] * c], 1), True, 5)


def ground_forest(key="ground_forest"):
    cv = tile_canvas(key)
    r = rng(key)
    grass_base(cv, P.mix(P.MOSS_DK, P.WOOD, 0.35), P.mix(P.FOREST_DK, P.WOOD_DK, 0.4), key, dark=P.FOREST_DK)
    # moss cushions (soft, close in value to the ground)
    moss_m = smoothstep(0.5, 0.6, cv.noise(160, "moss", 4))
    moss_m = ragged(cv, moss_m.astype(F32), 0.5, 2.5, 8, "mr")
    moss_m = cv.blur(moss_m, 3)
    wash(cv, moss_m, P.mix(P.MOSS, P.MOSS_DK, 0.6), 0.55, color2=P.mix(P.MOSS, P.MOSS_DK, 0.3), pool=0.4, pool_w=6,
         gran=0.3, gran_cell=2, key="mw", bloom=0.2)
    # leaf litter in drifts
    drifts = [(r.random() * 1024, r.random() * 1024) for _ in range(12)]
    leaf_cols = [P.HONEY, P.TERRACOTTA, P.mix(P.WOOD, P.HONEY, 0.4), P.mix(P.LEAF_YELLOW, P.MOSS, 0.3), P.BRICK]
    for g, lc in enumerate(leaf_cols):
        polys = []
        for i in range(55):
            dx, dy = drifts[r.integers(len(drifts))]
            x, y = dx + r.normal() * 70, dy + r.normal() * 50
            L = 20 + r.random() * 16
            a = r.random() * 6.28
            for X, Y in each_copy(cv, x, y, L):
                polys.append(leaf_poly(X, Y, L, a))
        m = cv.polys_mask(polys)
        drop_shadow(cv, m, 1.5, 2.5, 2, 0.35)
        paint(cv, m, lc, line=0.6, line_w=0.8, soft=2.0, key="lf%d" % g, ao=0, var=0.12, var_cell=10, hi=0.3)
    # twigs
    polys = []
    for i in range(70):
        x, y = r.random() * 1024, r.random() * 1024
        L = 30 + r.random() * 50
        a = r.random() * 6.28
        for X, Y in each_copy(cv, x, y, L):
            polys.append(stroke([(X, Y), (X + math.cos(a) * L * 0.5 + 3, Y + math.sin(a) * L * 0.5), (X + math.cos(a) * L, Y + math.sin(a) * L)],
                                3.2, 1.2, n=4))
    m = cv.polys_mask(polys)
    drop_shadow(cv, m, 1.5, 2, 1.5, 0.3)
    paint(cv, m, P.WOOD, line=0.5, line_w=0.7, soft=1.0, key="tw", ao=0)
    blades(cv, r, 700, 26, [P.MOSS, P.mix(P.MOSS, P.GRASS_LIGHT, 0.5)], key + "bl", 3.6, 0.8,
           cluster=[(r.random() * 1024, r.random() * 1024, 50) for _ in range(14)])
    # pebbles
    pebbles(cv, r, 40, 7, 14, P.STONE, "pb")
    return finish(cv, 0.013)


def pebbles(cv, r, n, rmin, rmax, col, key, where=None):
    polys = []
    for i in range(n):
        x, y = r.random() * cv.w, r.random() * cv.h
        if where is not None and where[int(y * cv.s) + cv.pady, int(x * cv.s) + cv.padx] < 0.6:
            continue
        rad = rmin + r.random() * (rmax - rmin)
        seed = r.integers(1 << 30)
        for X, Y in each_copy(cv, x, y, rad):
            polys.append(blob(X, Y, rad, rad * 0.75, np.random.default_rng(seed), 0.15, 7))
    m = cv.polys_mask(polys)
    drop_shadow(cv, m, 1.5, 2.5, 2, 0.4)
    paint(cv, m, col, line=0.6, line_w=0.9, soft=2.5, key=key, ao=0.2, hi=0.5, var=0.1, var_cell=8)


def voronoi_tile(W, n_cells, r):
    """Tileable jittered-grid Voronoi: returns (d1, d2, cell_id) for a WxW tile."""
    g = n_cells
    cs = W / g
    pts = (np.stack(np.meshgrid(np.arange(g), np.arange(g)), -1) + 0.15 + 0.7 * r.random((g, g, 2))) * cs
    yy, xx = np.mgrid[0:W, 0:W].astype(F32) + 0.5
    ci = (xx // cs).astype(int)
    cj = (yy // cs).astype(int)
    d1 = np.full((W, W), 1e9, F32)
    d2 = np.full((W, W), 1e9, F32)
    ids = np.zeros((W, W), np.int32)
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            ni = ci + di
            nj = cj + dj
            wi = ni % g
            wj = nj % g
            px = pts[wj, wi, 0] + (ni - wi) * cs
            py = pts[wj, wi, 1] + (nj - wj) * cs
            d = np.hypot(xx - px, yy - py).astype(F32)
            closer = d < d1
            d2 = np.where(closer, d1, np.minimum(d2, d))
            ids = np.where(closer, wj * g + wi, ids)
            d1 = np.where(closer, d, d1)
    return d1, d2, ids


def ground_shrine(key="ground_shrine"):
    cv = tile_canvas(key)
    r = rng(key)
    W = 1024
    d1, d2, ids = voronoi_tile(W, 6, r)
    edge = d2 - d1
    # pad to canvas via wrap indexing
    iy = (np.arange(cv.PH) - cv.pady) % W
    ix = (np.arange(cv.PW) - cv.padx) % W
    edge = edge[iy][:, ix]
    ids = ids[iy][:, ix]
    grout = np.ones((cv.PH, cv.PW), F32)
    wash(cv, grout, P.mix(P.MOSS_DK, P.STONE_DK, 0.5), 1.0, color2=P.MOSS_DK, pool=0, gran=0.2, key="gr")
    nz = cv.noise(12, "edge", 3)
    stone = smoothstep(7.0, 10.0, edge + (nz - 0.5) * 6)
    rs = np.random.default_rng(7)
    tints = rs.random(36 * 4)
    base = np.asarray(P.STONE_WARM, F32)
    cool = np.asarray(P.mix(P.STONE, P.VIOLET_FAR, 0.3), F32)
    t = tints[ids][..., None]
    field = base + (cool - base) * t
    # bevel shading from edge distance (pseudo-height)
    hgt = np.clip(edge / 26.0, 0, 1)
    gy, gx = np.gradient(cv.blur(hgt.astype(F32), 2))
    L = -(gx * P.LIGHT_DIR[0] + gy * P.LIGHT_DIR[1]) * 20
    field = field * (1.0 + np.clip(L, -0.35, 0.35))[..., None]
    field = field * (0.88 + 0.24 * cv.noise(40, "st", 4))[..., None]
    drop_shadow(cv, stone, 2, 3, 3, 0.4)
    wash(cv, stone, P.STONE_WARM, 1.0, field=field, pool=0.3, pool_w=2, gran=0.12, var=0.04, key="sw")
    # cracks
    ck = smoothstep(0.012, 0.0, np.abs(cv.noise(70, "ck", 4) - 0.5)) * stone * smoothstep(0.45, 0.6, cv.noise(200, "ckm", 2))
    wash(cv, ck.astype(F32), P.STONE_DK, 0.6, pool=0, gran=0.2, key="ckw")
    # moss creeping over the stone edges
    mz = smoothstep(14.0, 4.0, edge + (cv.noise(16, "mz", 3) - 0.5) * 14) * smoothstep(0.35, 0.6, cv.noise(150, "mzm", 3))
    mz = ragged(cv, mz.astype(F32), 0.4, 1.0, 3, "mzr")
    wash(cv, mz, P.MOSS, 0.85, color2=P.GRASS_LIGHT, pool=0.35, gran=0.25, key="mzw", bloom=0.3)
    blades(cv, r, 500, 12, [P.MOSS, P.GRASS_LIGHT], key + "bl", 2.2, 0.8,
           cluster=[(r.random() * 1024, r.random() * 1024, 30) for _ in range(20)])
    # fallen petals
    for i in range(40):
        x, y = r.random() * 1024, r.random() * 1024
        a = r.random() * 6.28
        for X, Y in each_copy(cv, x, y, 8):
            paint(cv, cv.mask(leaf_poly(X, Y, 8, a, 0.7)), P.ROSE, line=0.3, line_w=0.6, soft=1, key="pt", ao=0)
    return finish(cv, 0.013)


def ground_village(key="ground_village"):
    cv = tile_canvas(key)
    r = rng(key)
    earth0 = P.mix(P.mix(P.WOOD_LIGHT, P.CREAM_WARM, 0.35), P.STONE_WARM, 0.3)
    earth1 = P.mix(P.mix(P.WOOD_LIGHT, P.WOOD, 0.3), P.STONE_WARM, 0.25)
    grass_base(cv, earth0, earth1, key, dark=None)
    # compacted lighter patches & faint ruts
    lp = smoothstep(0.55, 0.7, cv.noise(90, "lp", 3))
    wash(cv, lp.astype(F32), P.light_of(earth0, 1.0), 0.45, pool=0.3, gran=0.2, key="lpw")
    # grass patches
    gm = smoothstep(0.62, 0.68, cv.noise(90, "gm", 4))
    gm = ragged(cv, gm.astype(F32), 0.7, 2.5, 6, "gmr")
    wash(cv, gm, P.mix(P.SAGE, P.MOSS, 0.4), 0.8, color2=P.mix(P.SAGE, P.GRASS_LIGHT, 0.5), pool=0.35, gran=0.25, key="gmw")
    # blades clustered in grass patches
    pts = []
    for i in range(400):
        x, y = r.random() * 1024, r.random() * 1024
        if gm[int(y) + cv.pady, int(x) + cv.padx] > 0.5:
            pts.append((x, y, 18))
    if pts:
        blades(cv, r, 1400, 24, [P.MOSS, P.GRASS_LIGHT, P.mix(P.MOSS, P.MOSS_DK, 0.5)], key + "bl", 3.4, 0.85, cluster=pts)
    pebbles(cv, r, 90, 5, 12, P.STONE_WARM, "pb")
    pebbles(cv, r, 30, 4, 8, P.mix(P.STONE, P.TERRACOTTA, 0.3), "pb2")
    return finish(cv, 0.013)


# ----------------------------------------------------------------------------------------
# decals (flat on the ground)
# ----------------------------------------------------------------------------------------
def decal_finish(cv):
    grain(cv, 0.02, key="paper")
    return to_image(cv.rgb, cv.a, bleed=True)


def path_centerline(W, H, r):
    xs = np.linspace(-40, W + 40, 24)
    ys = H / 2 + np.sin(xs / W * 2 * math.pi) * H * 0.16
    return np.stack([xs, ys], 1)


def path_mask(cv, r, width, key):
    c = path_centerline(cv.w, cv.h, r)
    s = stroke(c, width, width, n=6, cap=False)
    m = cv.mask(s)
    m = warp(cv, m, 10, 60, key + "w")
    m = ragged(cv, m, 0.5, 3, 10, key + "r")
    # ends cut straight so segments chain together
    return m


def decal_path_dirt(key="decal_path_dirt"):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    m = path_mask(cv, r, 210, key)
    fld = colfield(P.mix(P.WOOD_LIGHT, P.CREAM_WARM, 0.3), P.mix(P.WOOD_LIGHT, P.WOOD, 0.4),
                   smoothstep(0.3, 0.7, cv.noise(90, "d", 4)))
    wash(cv, m, P.WOOD_LIGHT, 0.95, field=fld, pool=0.45, pool_w=6, gran=0.15, key="dirt")
    # wheel ruts / centre grass strip
    c = path_centerline(cv.w, cv.h, r)
    for off in (-45, 45):
        s = stroke(c + (0, off), 16, 16, n=6, cap=False)
        mm = ragged(cv, warp(cv, cv.mask(s), 6, 40, "rt%d" % off), 0.5, 2, 8, "rr") * m
        wash(cv, mm, P.mix(P.WOOD, P.WOOD_LIGHT, 0.3), 0.5, pool=0.3, gran=0.2, key="rut")
    pts = [(float(x), float(y), 14) for x, y in c[::2]]
    blades(cv, r, 260, 18, [P.MOSS, P.GRASS_LIGHT], key + "bl", 3.0, 0.85, cluster=pts)
    pebbles(cv, r, 140, 4, 9, P.STONE_WARM, "pb", where=m)
    # grass tufts overhanging the edges
    edge_pts = []
    for x, y in c:
        for off in (-108, 108):
            edge_pts.append((float(x), float(y) + off, 16))
    blades(cv, r, 700, 18, [P.MOSS, P.GRASS_LIGHT, P.SAGE], key + "eb", 2.8, 0.9, cluster=edge_pts)
    return decal_finish(cv)


def decal_path_stone(key="decal_path_stone"):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    m = path_mask(cv, r, 200, key)
    wash(cv, m, P.mix(P.MOSS_DK, P.WOOD, 0.4), 0.9, pool=0.3, gran=0.2, key="bed")
    c = path_centerline(cv.w, cv.h, r)
    cc = catmull(c, closed=False, n=8)
    seg = np.diff(cc, axis=0)
    L = np.concatenate([[0], np.cumsum(np.hypot(seg[:, 0], seg[:, 1]))])
    stones = []
    d = 0
    while d < L[-1]:
        i = np.searchsorted(L, d)
        i = min(i, len(cc) - 2)
        p = cc[i]
        tdir = seg[i] / max(1e-6, np.hypot(*seg[i]))
        nrm = np.array([-tdir[1], tdir[0]])
        row = [-1, 1] if (int(d / 60) % 2) else [-1.6, 0, 1.6]
        for k in row:
            q = p + nrm * k * 46 + tdir * r.normal() * 6
            stones.append(blob(q[0], q[1], 30 + r.random() * 8, 24 + r.random() * 6, r, 0.12, 7,
                               rot=math.atan2(tdir[1], tdir[0])))
        d += 58 + r.random() * 8
    sm = cv.polys_mask(stones) * smoothstep(0.1, 0.5, m)
    drop_shadow(cv, sm, 2, 3, 3, 0.4)
    paint(cv, sm, P.STONE_WARM, line=0.6, line_w=1.2, soft=6, key="fs", var=0.12, var_cell=20, hi=0.4)
    mz = sm * smoothstep(0.6, 0.7, cv.noise(14, "mz", 3))
    wash(cv, mz.astype(F32), P.MOSS, 0.85, pool=0.3, gran=0.2, key="mz")
    edge_pts = []
    for x, y in c:
        for off in (-100, 100):
            edge_pts.append((float(x), float(y) + off, 16))
    blades(cv, r, 700, 18, [P.MOSS, P.GRASS_LIGHT, P.SAGE], key + "eb", 2.8, 0.9, cluster=edge_pts)
    return decal_finish(cv)


def decal_flowers(key="decal_flowers"):
    cv = Canvas(512, 512, 1.0, seed=key)
    r = rng(key)
    m = cv.mask(blob(256, 256, 200, 170, r, 0.15, 10))
    m = ragged(cv, warp(cv, m, 14, 60, "w"), 0.6, 6, 14, "r")
    wash(cv, m, P.mix(P.MOSS, P.GRASS_LIGHT, 0.4), 0.8, color2=P.MOSS, pool=0.3, gran=0.15, key="g")
    pts = [(256 + r.normal() * 70, 256 + r.normal() * 55, 16) for _ in range(40)]
    blades(cv, r, 700, 18, [P.MOSS, P.GRASS_LIGHT, P.MOSS_DK], key + "bl", 2.6, 0.9, cluster=pts)
    cols = [(P.WHITE_WARM, P.HONEY), (P.LEAF_YELLOW, P.AMBER), (P.ROSE, P.CREAM_WARM), (P.LAVENDER, P.CREAM),
            (P.mix(P.VERMILION, P.ROSE, 0.4), P.CREAM_WARM)]
    for i in range(110):
        x = 256 + r.normal() * 85
        y = 256 + r.normal() * 70
        pc, cc = cols[r.integers(len(cols))]
        tiny_flower(cv, x, y, 6 + r.random() * 5, pc, cc, "f%d" % i)
    return decal_finish(cv)


def decal_blight(key="decal_blight"):
    cv = Canvas(512, 512, 1.0, seed=key)
    r = rng(key)
    m = cv.mask(blob(256, 256, 190, 170, r, 0.25, 12))
    m = ragged(cv, warp(cv, m, 24, 50, "w"), 0.7, 8, 16, "r")
    rad = np.hypot(cv.xx() - 256, cv.yy() - 256) / 200
    fld = colfield(P.BLIGHT_DK, P.BLIGHT, np.clip(rad, 0, 1))
    wash(cv, m, P.BLIGHT, 0.85, field=fld, pool=0.5, pool_w=6, gran=0.25, key="b")
    # tendrils / veins
    polys = []
    for i in range(16):
        a = i / 16 * 6.28 + r.normal() * 0.2
        pts = [(256, 256)]
        x, y = 256, 256
        for k in range(6):
            a += r.normal() * 0.35
            x += math.cos(a) * 42
            y += math.sin(a) * 42
            pts.append((x, y))
        polys.append(stroke(pts, 9, 0.5, n=6))
    vm = cv.polys_mask(polys)
    wash(cv, vm, P.BLIGHT_VIOLET, 0.85, pool=0.3, pool_w=1.5, gran=0.15, key="v")
    core = cv.ellipse_mask(256, 256, 60, 50)
    core = cv.blur(core, 18)
    flat_fill(cv, core, P.BLIGHT_GLOW, 0.35)
    # ashen dead grass
    pts = [(256 + r.normal() * 110, 256 + r.normal() * 100, 30) for _ in range(20)]
    blades(cv, r, 260, 16, [P.BLIGHT, P.mix(P.BLIGHT, P.CREAM, 0.4)], key + "bl", 2.4, 0.8, cluster=pts)
    return decal_finish(cv)


# ----------------------------------------------------------------------------------------
# foreground (front-facing, isolated, transparent)
# ----------------------------------------------------------------------------------------
def fg_finish(cv):
    rim_light(cv, 0.35, 3)
    grain(cv, 0.025, key="paper")
    outline(cv, 2.6)
    return to_image(cv.rgb, cv.a)


def mossy_stone(cv, x, y, w, h, r, key, moss=0.7):
    pts = []
    n = 9
    for i in range(n):
        a = math.pi + i / (n - 1) * math.pi
        rr = 1.0 + (r.random() - 0.5) * 0.25
        pts.append((x + math.cos(a) * w * 0.5 * rr, y - 4 + math.sin(a) * h * rr))
    pts += [(x + w * 0.52, y), (x - w * 0.52, y)]
    m = cv.mask(catmull(pts, True, 8))
    m = warp(cv, m, w * 0.03, w * 0.2, key + "w")
    drop_shadow(cv, m, 6, 6, 10, 0.3)
    paint(cv, m, P.mix(P.STONE, P.VIOLET_FAR, 0.15), line=0.7, line_w=1.6, key=key, hi=0.45, ao=0.35, var=0.1,
          var_cell=w * 0.3, cel=0.45)
    if moss > 0:
        Y = cv.yy()
        top = y - h
        mm = m * smoothstep(top + h * (0.35 + 0.25 * cv.noise(w * 0.12, key + "mn", 3)), top + h * 0.15, Y)
        mm = ragged(cv, mm.astype(F32), 0.5, 1.5, 5, key + "mr")
        paint(cv, mm, P.MOSS, line=0.4, line_w=1.0, key=key + "m", soft=4, hi=0.5, ao=0, var=0.15, var_cell=8,
              light_col=P.GRASS_LIGHT)
    return m


def grass_tuft(cv, x, y, h, w, r, key, cols=None, n=24, lean=0.35):
    cols = cols or [P.MOSS_DK, P.MOSS, P.mix(P.MOSS, P.GRASS_LIGHT, 0.5), P.GRASS_LIGHT]
    groups = [[] for _ in cols]
    for i in range(n):
        t = (i + r.random()) / n
        bx = x + (t - 0.5) * w
        L = h * (0.45 + 0.55 * math.sin(math.pi * t) ** 0.6) * (0.75 + 0.35 * r.random())
        a = -math.pi / 2 + (t - 0.5) * 1.2 * lean * 2 + r.normal() * 0.12
        bend = (t - 0.5) * 0.8 + r.normal() * 0.2
        pts = [(bx, y + 2), (bx + math.cos(a) * L * 0.5, y + math.sin(a) * L * 0.5),
               (bx + math.cos(a + bend) * L, y + math.sin(a + bend) * L)]
        g = min(len(cols) - 1, int(r.random() * len(cols)))
        groups[g].append(stroke(pts, w / n * 2.2 + 3, 0.5, n=6, cap=False))
    m_all = np.zeros((cv.PH, cv.PW), F32)
    for g, polys in enumerate(groups):
        if polys:
            m = cv.polys_mask(polys)
            paint(cv, m, cols[g], line=0.6, line_w=1.0, key=key + str(g), soft=3, ao=0.5, hi=0.4, var=0.06)
            m_all = np.maximum(m_all, m)
    return m_all


def flower_head(cv, x, y, rad, petal, center, key, n=6):
    polys = []
    for k in range(n):
        a = k * 2 * math.pi / n + 0.3
        polys.append(ellipse(x + math.cos(a) * rad * 0.58, y + math.sin(a) * rad * 0.5, rad * 0.5, rad * 0.32, a, 14))
    m = cv.polys_mask(polys)
    paint(cv, m, petal, line=0.6, line_w=1.0, soft=rad * 0.3, key=key, ao=0.2, hi=0.5, var=0.05)
    paint(cv, cv.ellipse_mask(x, y, rad * 0.28, rad * 0.25), center, line=0.5, line_w=0.8, soft=2, key=key + "c", ao=0)


def bell_flower(cv, x, y, rad, col, key):
    p = np.array([(x - rad * 0.7, y - rad * 0.6), (x, y - rad * 0.9), (x + rad * 0.7, y - rad * 0.6), (x + rad * 0.85, y + rad * 0.3),
                  (x + rad * 0.4, y + rad * 0.15), (x, y + rad * 0.45), (x - rad * 0.4, y + rad * 0.15), (x - rad * 0.85, y + rad * 0.3)])
    paint(cv, cv.mask(catmull(p, True, 6)), col, line=0.6, line_w=1.0, soft=rad * 0.3, key=key, hi=0.5)


def fg_stones(key, variant):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    base = 488
    if variant == 0:
        items = [(420, 300, 330), (650, 220, 220), (250, 160, 150), (820, 120, 110)]
    else:
        items = [(520, 360, 260), (300, 240, 190), (760, 200, 170)]
    for i, (x, w, h) in enumerate(sorted(items, key=lambda t: -t[2])):
        mossy_stone(cv, x, base - r.random() * 8, w, h, r, "s%d" % i)
    for i in range(5):
        grass_tuft(cv, 100 + r.random() * 820, base + 4, 70 + r.random() * 50, 90 + r.random() * 60, r, "gt%d" % i, n=16)
    return fg_finish(cv)


def fg_grass(key, variant):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    base = 490
    tufts = 9 if variant == 0 else 6
    order = []
    for i in range(tufts):
        x = 90 + (i + r.random() * 0.6) * (840 / tufts)
        h = 220 + r.random() * 200 if variant == 0 else 260 + r.random() * 160
        w = 140 + r.random() * 120
        order.append((h, x, w))
    for i, (h, x, w) in enumerate(sorted(order, reverse=True)):
        grass_tuft(cv, x, base, h, w, r, "t%d" % i, n=30)
    return fg_finish(cv)


def fg_flowers(key, variant):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    base = 490
    for i in range(6):
        grass_tuft(cv, 120 + i * 150 + r.random() * 40, base, 150 + r.random() * 90, 140, r, "g%d" % i, n=20)
    palette_sets = [
        [(P.WHITE_WARM, P.HONEY), (P.hx("f6cf4a"), P.AMBER), (P.hx("ee8fa0"), P.CREAM_WARM)],
        [(P.hx("a98be0"), P.CREAM), (P.hx("ec6d5a"), P.CREAM_WARM), (P.hx("f4d35e"), P.TERRACOTTA)],
    ][variant]
    stems = []
    heads = []
    for i in range(16):
        x = 90 + r.random() * 840
        h = 140 + r.random() * 260
        bend = r.normal() * 40
        stems.append(stroke([(x, base), (x + bend * 0.4, base - h * 0.5), (x + bend, base - h)], 6, 3.5, n=6))
        # leaves
        for k in range(2):
            ly = base - h * (0.25 + 0.25 * k)
            side = 1 if k % 2 else -1
            stems.append(leaf_poly(x + bend * 0.2 * (1 + k), ly, 36 + r.random() * 16, -math.pi / 2 + side * 1.0, 0.35))
        heads.append((x + bend, base - h, 26 + r.random() * 16, palette_sets[i % len(palette_sets)], i))
    sm = cv.polys_mask(stems)
    paint(cv, sm, P.MOSS, line=0.6, line_w=1.0, soft=3, key="stem", ao=0.3)
    for (x, y, rad, (pc, cc), i) in heads:
        if variant == 1 and i % 3 == 1:
            for k in range(3):
                bell_flower(cv, x + k * 10 - 10, y + k * 22, rad * 0.55, pc, "bf%d%d" % (i, k))
        else:
            flower_head(cv, x, y, rad, pc, cc, "fh%d" % i, n=5 + (i % 3))
    return fg_finish(cv)


def fg_ferns(key="fg_ferns"):
    cv = Canvas(1024, 512, 1.0, seed=key)
    r = rng(key)
    for i, (x, h) in enumerate([(300, 420), (650, 460), (480, 330), (820, 300), (170, 280)]):
        fern(cv, x, 495, h, r, "f%d" % i, P.mix(P.MOSS, P.MOSS_DK, 0.3 + 0.15 * (i % 3)))
    return fg_finish(cv)


# ----------------------------------------------------------------------------------------
# registration
# ----------------------------------------------------------------------------------------
BACKGROUNDS = {
    "bg_clouds": (bg_clouds, (2048, 1024), 6),
    "bg_mountains_far": (bg_mountains_far, (2048, 1024), 9),
    "bg_hills_far": (bg_hills_far, (2048, 1024), 7),
    "bg_hills_near": (bg_hills_near, (2048, 1024), 6),
    "bg_forest_far": (bg_forest_far, (2048, 1024), 8),
    "bg_forest_near": (bg_forest_near, (2048, 1024), 8),
    "bg_shrine_cliffs": (bg_shrine_cliffs, (2048, 1024), 9),
    "bg_village_far": (bg_village_far, (2048, 512), 6),
}

GROUND_TILE_METRES = 6.0


def _one(fn, key, *a):
    return {key: fn(key, *a) if a else fn(key)}


def register(reg):
    for k, (fn, size, h) in BACKGROUNDS.items():
        reg.spec(k, "Background", size=size, height=h, pivot=(0.5, 0.0), loop=True)
        reg.job([k], _one, fn, k)
    for k, fn in (("ground_meadow", ground_meadow), ("ground_forest", ground_forest), ("ground_shrine", ground_shrine),
                  ("ground_village", ground_village)):
        reg.spec(k, "Ground", size=(1024, 1024), height=GROUND_TILE_METRES, pivot=(0.5, 0.0), tile_y=True, loop=False)
        reg.job([k], _one, fn, k)
    # decals: flat on the ground; height = world size of the image's vertical axis
    reg.spec("decal_path_dirt", "Prop", size=(1024, 512), height=4.0, pivot=(0.5, 0.5))
    reg.job(["decal_path_dirt"], _one, decal_path_dirt, "decal_path_dirt")
    reg.spec("decal_path_stone", "Prop", size=(1024, 512), height=4.0, pivot=(0.5, 0.5))
    reg.job(["decal_path_stone"], _one, decal_path_stone, "decal_path_stone")
    reg.spec("decal_flowers", "Prop", size=(512, 512), height=3.0, pivot=(0.5, 0.5))
    reg.job(["decal_flowers"], _one, decal_flowers, "decal_flowers")
    reg.spec("decal_blight", "Prop", size=(512, 512), height=4.0, pivot=(0.5, 0.5))
    reg.job(["decal_blight"], _one, decal_blight, "decal_blight")
    fgs = {
        "fg_stones_a": (fg_stones, 0, 1.2), "fg_stones_b": (fg_stones, 1, 1.2),
        "fg_grass_a": (fg_grass, 0, 1.0), "fg_grass_b": (fg_grass, 1, 1.0),
        "fg_flowers_a": (fg_flowers, 0, 1.0), "fg_flowers_b": (fg_flowers, 1, 1.0),
    }
    for k, (fn, v, h) in fgs.items():
        reg.spec(k, "Foreground", size=(1024, 512), measure=h, pivot=(0.5, 0.04))
        reg.job([k], _one, fn, k, v)
    reg.spec("fg_ferns", "Foreground", size=(1024, 512), measure=1.3, pivot=(0.5, 0.04))
    reg.job(["fg_ferns"], _one, fg_ferns, "fg_ferns")
