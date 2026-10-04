"""Characters: Final Fantasy X-inspired heroes, companions and villagers on a shared anime-proportioned rig.

Every humanoid is drawn on a 512x1024 design canvas that represents 2.2 m of world height (feet on the
pivot line at 5 % from the bottom), rendered at 2x and downsampled for clean line work. Portraits are
256x256 bust crops taken from the same 2x render.

Design language (FFX-inspired, no copied characters): stylised anime proportions (~6.5 heads), layered
fabrics with belts, straps, trims and asymmetric details, expressive hair, strong class colour identity.
"""
import math

import numpy as np

import palette as P
from brushes import (Canvas, F32, bbox, blob, blur, catmull, colfield, downsample, drop_shadow, ellipse, flat_fill,
                     glow, grain, outline, paint, ragged, rim_light, rng, rotate, smoothstep, stroke, to_image, warp,
                     wash)

CANVAS_M = 2.2
PPM = 1024.0 / CANVAS_M
FEET = 1024.0 * 0.95
SS = 2  # render scale


def A(pts):
    return np.asarray(pts, dtype=np.float64)


def skin_lo(c):
    return P.mix(P.mix(P.scale_v(c, 0.80), P.ROSE, 0.30), P.VIOLET, 0.08)


def dark_line(c):
    return P.mix(c, P.INK, 0.62)


def stroke_w(pts, ws, n=8, cap=True):
    """Open Catmull-Rom tube through pts with a width per control point."""
    pts = A(pts)
    c = catmull(pts, closed=False, n=n) if len(pts) >= 3 else np.linspace(pts[0], pts[1], n + 1)
    m = len(c)
    segs = len(pts) - 1
    w = np.zeros(m)
    for i in range(m):
        s = min(segs - 1, i // n)
        u = (i - s * n) / float(n)
        if i == m - 1:
            s, u = segs - 1, 1.0
        w[i] = ws[s] * (1 - u) + ws[s + 1] * u
    seg = np.gradient(c, axis=0)
    seg /= np.maximum(np.hypot(seg[:, 0], seg[:, 1]), 1e-9)[:, None]
    nrm = np.stack([-seg[:, 1], seg[:, 0]], 1)
    left = c + nrm * (w / 2)[:, None]
    right = c - nrm * (w / 2)[:, None]
    out = [left]
    if cap and w[-1] > 0.5:
        a = math.atan2(seg[-1, 1], seg[-1, 0])
        out.append(ellipse(c[-1, 0], c[-1, 1], w[-1] / 2, w[-1] / 2, 0, 9, a + math.pi / 2, a - math.pi / 2)[1:-1])
    out.append(right[::-1])
    if cap and w[0] > 0.5:
        a = math.atan2(seg[0, 1], seg[0, 0])
        out.append(ellipse(c[0, 0], c[0, 1], w[0] / 2, w[0] / 2, 0, 9, a - math.pi / 2, a - 3 * math.pi / 2)[1:-1])
    return np.concatenate(out)


class Look:
    """Appearance parameters for one humanoid."""

    def __init__(self, **kw):
        self.height = 1.8
        self.heads = 6.6
        self.build = "m"          # m | f | big | child | old
        self.skin = P.SKIN_LIGHT
        self.hair = P.hx("4a3328")
        self.eyes = P.hx("8a5a3c")
        self.hair_style = "short"
        self.turn = 0.18          # face turned toward screen right
        self.expr = "calm"        # calm | smile | grin | stern
        self.beard = None
        self.cx = 256.0
        for k, v in kw.items():
            setattr(self, k, v)


BUILDS = {
    #       shoulder chest waist hip  thigh knee calf ankle  upperarm forearm wrist neck
    "m":     (0.86, 0.76, 0.58, 0.62, 0.30, 0.17, 0.20, 0.11, 0.25, 0.21, 0.13, 0.19),
    "f":     (0.70, 0.64, 0.45, 0.66, 0.29, 0.155, 0.18, 0.10, 0.20, 0.17, 0.11, 0.16),
    "big":   (1.08, 0.98, 0.84, 0.84, 0.38, 0.22, 0.26, 0.14, 0.33, 0.29, 0.18, 0.26),
    "child": (0.64, 0.60, 0.54, 0.58, 0.26, 0.16, 0.18, 0.11, 0.19, 0.17, 0.11, 0.17),
    "old":   (0.80, 0.74, 0.66, 0.64, 0.28, 0.17, 0.18, 0.11, 0.22, 0.19, 0.12, 0.18),
}

CLOTH = dict(line=0.95, line_w=1.0, cel=0.62, ao=0.14, hi=0.32, shade=0.9, var=0.02, var_cell=30)


class Fig:
    def __init__(self, cv, look):
        self.cv = cv
        self.L = look
        self.r = rng(cv.seed, "fig")
        H = look.height * PPM
        self.H = H
        self.feet = FEET
        self.top = FEET - H
        self.hu = H / look.heads
        hu = self.hu
        body = H - hu
        self.chin = self.top + hu
        f = lambda k: self.chin + body * k
        self.neck_y = f(0.036)
        self.sh_y = f(0.085)
        self.chest_y = f(0.18)
        self.waist_y = f(0.31)
        self.hip_y = f(0.39)
        self.crotch_y = f(0.43)
        self.knee_y = f(0.70)
        self.ankle_y = f(0.955)
        b = BUILDS[look.build]
        self.w_sh, self.w_chest, self.w_waist, self.w_hip = [v * hu for v in b[:4]]
        self.w_thigh, self.w_knee, self.w_calf, self.w_ankle = [v * hu * 2 for v in b[4:8]]
        self.w_uarm, self.w_farm, self.w_wrist, self.w_neck = [v * hu * 2 for v in b[8:12]]
        self.cx = look.cx
        self.head_c = (self.cx + look.turn * hu * 0.05, self.top + hu * 0.5)
        self.hw = hu * 0.44   # head half width
        self.hh = hu * 0.5    # head half height
        # default leg joints
        sx = self.w_hip * 0.5
        self.hipL = (self.cx - sx, self.hip_y + hu * 0.1)
        self.hipR = (self.cx + sx, self.hip_y + hu * 0.1)
        self.kneeL = (self.cx - sx - hu * 0.05, self.knee_y)
        self.kneeR = (self.cx + sx + hu * 0.12, self.knee_y)
        self.ankL = (self.cx - sx - hu * 0.08, self.ankle_y)
        self.ankR = (self.cx + sx + hu * 0.28, self.ankle_y)
        self.shL = (self.cx - self.w_sh + hu * 0.14, self.sh_y + hu * 0.12)
        self.shR = (self.cx + self.w_sh - hu * 0.14, self.sh_y + hu * 0.12)

    # ------------------------------------------------------------------ painting shortcuts
    def shape(self, pts, col, smooth=True, shadow=0.22, n=8, **kw):
        cv = self.cv
        p = catmull(A(pts), True, n) if smooth else A(pts)
        m = cv.mask(p)
        if shadow:
            drop_shadow(cv, m, 1.6, 3.2, 3.0, shadow)
        k = dict(CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def tube(self, pts, ws, col, shadow=0.22, cap=True, **kw):
        cv = self.cv
        m = cv.mask(stroke_w(pts, ws, cap=cap))
        if shadow:
            drop_shadow(cv, m, 1.6, 3.2, 3.0, shadow)
        k = dict(CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def band(self, pts, w, col, shadow=0.2, w1=None, **kw):
        return self.tube(pts, [w] * (len(pts) - 1) + [w if w1 is None else w1], col, shadow=shadow, **kw)

    def ell(self, cx, cy, rx, ry, col, rot=0.0, shadow=0.0, **kw):
        cv = self.cv
        m = cv.ellipse_mask(cx, cy, rx, ry, rot)
        if shadow:
            drop_shadow(cv, m, 1.5, 3, 3, shadow)
        k = dict(CLOTH)
        k.update(kw)
        paint(cv, m, col, **k)
        return m

    def skin_part(self, pts, smooth=True, **kw):
        k = dict(lo=skin_lo(self.L.skin), line_col=P.mix(skin_lo(self.L.skin), P.INK, 0.45), hi=0.35, cel=0.6)
        k.update(kw)
        return self.shape(pts, self.L.skin, smooth=smooth, **k)

    def skin_tube(self, pts, ws, **kw):
        k = dict(lo=skin_lo(self.L.skin), line_col=P.mix(skin_lo(self.L.skin), P.INK, 0.45), hi=0.35, cel=0.6)
        k.update(kw)
        return self.tube(pts, ws, self.L.skin, **k)

    def folds(self, mask, segs, col, alpha=0.35, w=1.4):
        cv = self.cv
        m = cv.lines_mask(segs, w) * mask
        m = cv.blur(m, 0.6)
        cv.atop(P.shadow_of(col, 1.3), m * alpha)

    def lines(self, segs, w, col, alpha=0.8, clip=None):
        cv = self.cv
        m = cv.lines_mask(segs, w)
        if clip is not None:
            m = m * clip
        cv.atop(col, m * alpha)

    def glow(self, x, y, r, col, s=0.5, clip=False):
        """Clipped glows paint now; free glows are deferred until after the outline pass (no dark halo)."""
        if clip:
            glow(self.cv, x, y, r, col, s, clip=True)
        else:
            if not hasattr(self.cv, "post_glow"):
                self.cv.post_glow = []
            self.cv.post_glow.append((x, y, r, col, s))

    # ------------------------------------------------------------------ body
    def leg(self, side, col, bare=False, w_scale=1.0):
        hip, knee, ank = (self.hipL, self.kneeL, self.ankL) if side < 0 else (self.hipR, self.kneeR, self.ankR)
        ws = [self.w_thigh * w_scale, self.w_knee * 1.15 * w_scale, self.w_ankle * 1.1 * w_scale]
        mid_thigh = ((hip[0] + knee[0]) / 2 - side * self.hu * 0.02, (hip[1] + knee[1]) / 2)
        calf = ((knee[0] + ank[0]) / 2 + side * self.hu * 0.03, knee[1] + (ank[1] - knee[1]) * 0.3)
        pts = [hip, mid_thigh, knee, calf, ank]
        wl = [ws[0], ws[0] * 0.92, ws[1], self.w_calf * w_scale, ws[2]]
        if bare:
            return self.skin_tube(pts, wl)
        return self.tube(pts, wl, col)

    def pelvis(self, col, top=None, bottom=None):
        hu = self.hu
        top = top or self.waist_y + hu * 0.1
        bottom = bottom or self.crotch_y + hu * 0.18
        pts = [(self.cx - self.w_waist * 0.98, top), (self.cx + self.w_waist * 0.98, top), (self.cx + self.w_hip, self.hip_y + hu * 0.05),
               (self.cx + self.w_hip * 0.85, bottom - hu * 0.05), (self.cx, bottom), (self.cx - self.w_hip * 0.85, bottom - hu * 0.05),
               (self.cx - self.w_hip, self.hip_y + hu * 0.05)]
        return self.shape(pts, col)

    def boot(self, side, col, top_y=None, cuff=None, toe=1.0, w_scale=1.0):
        hu = self.hu
        ank = self.ankL if side < 0 else self.ankR
        knee = self.kneeL if side < 0 else self.kneeR
        top_y = top_y if top_y is not None else knee[1] + hu * 0.35
        t = (top_y - knee[1]) / (ank[1] - knee[1])
        tx = knee[0] + (ank[0] - knee[0]) * t
        wt = (self.w_calf * 1.12 + hu * 0.06) * w_scale
        foot_dx = side * hu * 0.1 * toe
        pts = [(tx - wt / 2, top_y), (tx + wt / 2, top_y), (ank[0] + self.w_ankle * 0.65, ank[1] - hu * 0.05),
               (ank[0] + foot_dx + hu * 0.2, self.feet - hu * 0.07), (ank[0] + foot_dx + hu * 0.15, self.feet + 1),
               (ank[0] + foot_dx - hu * 0.17, self.feet + 1), (ank[0] - self.w_ankle * 0.7, self.feet - hu * 0.08),
               (ank[0] - self.w_ankle * 0.65, ank[1] - hu * 0.05)]
        m = self.shape(pts, col, smooth=True, n=6)
        # sole
        sole = [(ank[0] + foot_dx - hu * 0.18, self.feet - hu * 0.03), (ank[0] + foot_dx + hu * 0.21, self.feet - hu * 0.03),
                (ank[0] + foot_dx + hu * 0.19, self.feet + 2), (ank[0] + foot_dx - hu * 0.17, self.feet + 2)]
        self.shape(sole, P.mix(col, P.INK, 0.5), smooth=False, shadow=0, line=0.5)
        if cuff:
            cp = [(tx - wt / 2 - hu * 0.04, top_y - hu * 0.02), (tx + wt / 2 + hu * 0.04, top_y - hu * 0.02),
                  (tx + wt / 2 + hu * 0.02, top_y + hu * 0.16), (tx - wt / 2 - hu * 0.02, top_y + hu * 0.16)]
            self.shape(cp, cuff, smooth=False)
        return m

    def torso(self, col, top=None, bottom=None, neckline=0.0, bare=False):
        hu = self.hu
        cx = self.cx
        top = top if top is not None else self.sh_y
        bottom = bottom if bottom is not None else self.hip_y
        pts = [(cx - self.w_neck * 0.6, self.neck_y + hu * 0.05), (cx - self.w_sh + hu * 0.06, top + hu * 0.06),
               (cx - self.w_sh, top + hu * 0.3), (cx - self.w_chest, self.chest_y + hu * 0.1), (cx - self.w_waist, self.waist_y),
               (cx - self.w_hip * 0.95, bottom), (cx + self.w_hip * 0.95, bottom), (cx + self.w_waist, self.waist_y),
               (cx + self.w_chest, self.chest_y + hu * 0.1), (cx + self.w_sh, top + hu * 0.3), (cx + self.w_sh - hu * 0.06, top + hu * 0.06),
               (cx + self.w_neck * 0.6, self.neck_y + hu * 0.05)]
        if neckline:
            pts.append((cx, self.neck_y + hu * neckline))
        if bare:
            return self.skin_part(pts)
        return self.shape(pts, col)

    def arm(self, side, elbow, wrist, sleeve=None, bare_from=0.0, glove=None, cuff=None, w_scale=1.0, sleeve_w=1.0):
        """Shoulder->elbow->wrist. sleeve colour covers the arm; bare_from (0..1) leaves the forearm bare."""
        sh = self.shL if side < 0 else self.shR
        ws = [self.w_uarm * w_scale, self.w_uarm * 0.92 * w_scale, self.w_farm * 0.95 * w_scale, self.w_wrist * w_scale]
        mid = ((sh[0] + elbow[0]) / 2, (sh[1] + elbow[1]) / 2)
        pts = [sh, mid, elbow, wrist]
        self.skin_tube(pts, ws)
        if sleeve is not None:
            sw = [w * sleeve_w * 1.12 for w in ws]
            if bare_from > 0:
                # sleeve to the elbow
                fe = (elbow[0] + (wrist[0] - elbow[0]) * (bare_from - 0.5) * 2, elbow[1] + (wrist[1] - elbow[1]) * (bare_from - 0.5) * 2) \
                    if bare_from > 0.5 else elbow
                self.tube([sh, mid, fe], [sw[0], sw[1], sw[2]], sleeve)
            else:
                self.tube(pts, sw, sleeve)
        if glove is not None:
            gs = (elbow[0] + (wrist[0] - elbow[0]) * 0.45, elbow[1] + (wrist[1] - elbow[1]) * 0.45)
            self.tube([gs, wrist], [ws[2] * 1.18, ws[3] * 1.25], glove)
            if cuff:
                cdx, cdy = wrist[0] - gs[0], wrist[1] - gs[1]
                self.tube([(gs[0] - cdx * 0.06, gs[1] - cdy * 0.06), (gs[0] + cdx * 0.1, gs[1] + cdy * 0.1)], [ws[2] * 1.32, ws[2] * 1.28], cuff,
                          hi=0.5, cap=False)

    def hand(self, x, y, ang=0.0, fist=True, col=None, s=1.0):
        """Simple anime hand. ang: direction the fingers point (radians, 0 = down)."""
        hu = self.hu * s
        col = col or self.L.skin
        ca, sa = math.cos(ang), math.sin(ang)

        def T(px, py):
            return (x + px * ca - py * sa, y + px * sa + py * ca)
        if fist:
            pts = [T(-0.13 * hu, -0.04 * hu), T(0.13 * hu, -0.05 * hu), T(0.15 * hu, 0.12 * hu), T(0.1 * hu, 0.22 * hu),
                   T(-0.1 * hu, 0.22 * hu), T(-0.15 * hu, 0.1 * hu)]
        else:
            pts = [T(-0.12 * hu, -0.04 * hu), T(0.12 * hu, -0.04 * hu), T(0.14 * hu, 0.2 * hu), T(0.08 * hu, 0.32 * hu),
                   T(-0.06 * hu, 0.31 * hu), T(-0.13 * hu, 0.18 * hu), T(-0.2 * hu, 0.1 * hu)]
        if col == self.L.skin:
            m = self.skin_part(pts, shadow=0.18)
        else:
            m = self.shape(pts, col, shadow=0.18)
        if fist:
            segs = [(T(-0.1 * hu, 0.06 * hu)[0], T(-0.1 * hu, 0.06 * hu)[1], T(0.11 * hu, 0.05 * hu)[0], T(0.11 * hu, 0.05 * hu)[1])]
            self.lines(segs, max(0.8, hu * 0.012), dark_line(col), 0.5, clip=m)
        return m

    def neck(self, col=None):
        hu = self.hu
        hx = self.head_c[0]
        pts = [(hx - self.w_neck / 2, self.chin - hu * 0.25), (hx + self.w_neck / 2, self.chin - hu * 0.25),
               (self.cx + self.w_neck * 0.62, self.sh_y + hu * 0.1), (self.cx - self.w_neck * 0.62, self.sh_y + hu * 0.1)]
        if col is not None:
            m = self.shape(pts, col, smooth=False, shadow=0)
            lo = P.shadow_of(col, 1.0)
        else:
            m = self.skin_part(pts, smooth=False, shadow=0)
            lo = skin_lo(self.L.skin)
        # shadow under the chin
        sh = self.cv.mask(ellipse(hx + hu * 0.03, self.chin - hu * 0.02, self.w_neck * 0.75, hu * 0.12))
        self.cv.atop(lo, sh * m * 0.75)
        return m

    # ------------------------------------------------------------------ head & face
    def face_pts(self):
        hx, hy = self.head_c
        hw, hh = self.hw, self.hh
        t = self.L.turn
        ch = 1.0 if self.L.build != "child" else 0.92
        return [(hx - hw * 0.15, hy - hh), (hx + hw * 0.6, hy - hh * 0.85), (hx + hw * 0.98, hy - hh * 0.35),
                (hx + hw * (0.97 - t * 0.3), hy + hh * 0.12), (hx + hw * (0.8 - t * 0.25), hy + hh * 0.48),
                (hx + hw * (0.42 - t * 0.1) * ch, hy + hh * 0.8), (hx + hw * t * 0.35, hy + hh * 0.98 * ch),
                (hx - hw * 0.4 * ch, hy + hh * 0.82), (hx - hw * 0.86, hy + hh * 0.46), (hx - hw * 1.0, hy + hh * 0.08),
                (hx - hw * 0.97, hy - hh * 0.4), (hx - hw * 0.7, hy - hh * 0.85)]

    def ears(self, pointed=0.0):
        hx, hy = self.head_c
        hw, hh = self.hw, self.hh
        for side in (-1, 1):
            if side > 0 and self.L.turn > 0.12:
                ex = hx + hw * 0.93
            else:
                ex = hx + side * hw * 0.98
            pts = [(ex, hy - hh * 0.08), (ex + side * hw * (0.16 + pointed * 0.5), hy - hh * (0.12 + pointed * 0.45)),
                   (ex + side * hw * 0.18, hy + hh * 0.1), (ex + side * hw * 0.06, hy + hh * 0.3), (ex - side * hw * 0.05, hy + hh * 0.22)]
            self.skin_part(pts, shadow=0)

    def face(self):
        cv = self.cv
        hx, hy = self.head_c
        m = self.skin_part(self.face_pts(), shadow=0, soft=self.hw * 0.35, cel=0.7, hi=0.25)
        # soft cheek shading on the far side & under the hairline
        t = self.L.turn
        sh = cv.mask(ellipse(hx + self.hw * 0.95, hy + self.hh * 0.2, self.hw * 0.35, self.hh * 0.7))
        cv.atop(skin_lo(self.L.skin), cv.blur(sh, self.hw * 0.12) * m * 0.22 * min(1, t * 4))
        return m

    def eye(self, cx, cy, w, h, d, iris, lash, female=True, look=0.12):
        """d = -1 for the eye on screen left (outer corner left), +1 for screen right."""
        cv = self.cv
        hu = self.hu
        pts = [(cx - d * w * 0.5, cy - h * 0.05), (cx - d * w * 0.15, cy - h * 0.55), (cx + d * w * 0.35, cy - h * 0.45),
               (cx + d * w * 0.5, cy + h * 0.05), (cx + d * w * 0.15, cy + h * 0.48), (cx - d * w * 0.3, cy + h * 0.35)]
        sc = cv.mask(catmull(A(pts), True, 6))
        paint(cv, sc, P.hx("fdf8f2"), line=0.0, soft=2, ao=0, var=0, flat=True)
        # lid shadow on the sclera
        lid = sc * smoothstep(cy - h * 0.1, cy - h * 0.55, cv.yy())
        cv.atop(P.mix(skin_lo(self.L.skin), P.LAVENDER, 0.4), lid * 0.6)
        ix = cx + look * w
        rx, ry = w * 0.27, h * 0.5
        im = cv.ellipse_mask(ix, cy + h * 0.02, rx, ry) * sc
        grad = smoothstep(cy - ry, cy + ry, cv.yy())
        col = colfield(P.mix(iris, P.INK, 0.65), P.light_of(iris, 1.3), np.broadcast_to(grad, im.shape))
        bb = bbox(im, 1)
        if bb is not None:
            ys, xs = bb
            cv.over(col[ys, xs], im[ys, xs], (ys, xs))
        pm = cv.ellipse_mask(ix, cy - h * 0.02, rx * 0.45, ry * 0.5) * sc
        flat_fill(cv, pm, P.mix(iris, P.INK, 0.85), 0.9)
        # iris ring
        ring = np.clip(cv.ellipse_mask(ix, cy + h * 0.02, rx, ry) - cv.ellipse_mask(ix, cy + h * 0.02, rx * 0.85, ry * 0.88), 0, 1) * sc
        cv.atop(P.mix(iris, P.INK, 0.7), ring * 0.7)
        # highlights
        flat_fill(cv, cv.ellipse_mask(ix - rx * 0.35, cy - ry * 0.35, rx * 0.32, ry * 0.26) * sc, P.WHITE_WARM, 0.95)
        flat_fill(cv, cv.ellipse_mask(ix + rx * 0.35, cy + ry * 0.45, rx * 0.15, ry * 0.12) * sc, P.WHITE_WARM, 0.8)
        # upper lash line with a flick at the outer corner
        lw = h * (0.32 if female else 0.24)
        up = [(cx + d * w * 0.55, cy - h * 0.05), (cx + d * w * 0.32, cy - h * 0.5), (cx - d * w * 0.15, cy - h * 0.58),
              (cx - d * w * 0.48, cy - h * 0.12), (cx - d * w * 0.62 - (d * w * 0.05 if female else 0), cy - h * (0.28 if female else 0.0))]
        cv_m = cv.mask(stroke_w(up, [lw * 0.25, lw * 0.75, lw, lw * 0.9, lw * 0.25]))
        flat_fill(cv, cv_m, lash, 1.0)
        low = [(cx - d * w * 0.45, cy + h * 0.25), (cx - d * w * 0.1, cy + h * 0.45), (cx + d * w * 0.25, cy + h * 0.42)]
        flat_fill(cv, cv.mask(stroke_w(low, [lw * 0.3, lw * 0.25, lw * 0.05])), lash, 0.55)

    def brow(self, cx, cy, w, d, col, thick, angle=0.0):
        pts = [(cx + d * w * 0.45, cy + w * 0.05), (cx - d * w * 0.05, cy - w * 0.12 - angle * w), (cx - d * w * 0.55, cy - w * 0.02 - angle * w * 1.4)]
        m = self.cv.mask(stroke_w(pts, [thick * 0.6, thick, thick * 0.45]))
        flat_fill(self.cv, m, col, 0.95)

    def features(self, female=None):
        cv = self.cv
        L = self.L
        hx, hy = self.head_c
        hw, hh = self.hw, self.hh
        t = L.turn
        female = (L.build == "f" or L.build == "child") if female is None else female
        ey = hy + hh * 0.18
        ew = hw * (0.46 if female else 0.42)
        eh = hw * (0.3 if female else 0.22)
        if L.build == "child":
            ew, eh = hw * 0.5, hw * 0.36
        if L.build == "old":
            eh *= 0.7
        lash = P.mix(L.hair, P.INK, 0.7)
        sep = hw * 0.47
        ex1 = hx + t * hw * 0.45 - sep
        ex2 = hx + t * hw * 0.45 + sep * (1 - t * 0.35)
        if getattr(L, "scar", False):
            # closed eye crossed by a pale vertical scar
            cl = [(ex1 - ew * 0.5, ey + eh * 0.05), (ex1, ey + eh * 0.25), (ex1 + ew * 0.5, ey + eh * 0.0)]
            flat_fill(cv, cv.mask(stroke_w(cl, [eh * 0.12, eh * 0.22, eh * 0.12])), lash, 1.0)
            sc = [(ex1 + ew * 0.05, ey - eh * 2.4), (ex1 - ew * 0.02, ey), (ex1 - ew * 0.1, ey + eh * 2.0)]
            scm = cv.mask(stroke_w(sc, [hw * 0.035, hw * 0.06, hw * 0.03]))
            flat_fill(cv, scm, P.mix(L.skin, P.WHITE_WARM, 0.55), 0.95)
            flat_fill(cv, np.clip(cv.mask(stroke_w([(p[0] + hw * 0.04, p[1]) for p in sc], [hw * 0.02, hw * 0.03, hw * 0.015])), 0, 1),
                      skin_lo(L.skin), 0.6)
        else:
            self.eye(ex1, ey, ew, eh, -1, L.eyes, lash, female)
        self.eye(ex2, ey, ew * (1 - t * 0.55), eh, 1, L.eyes, lash, female)
        bcol = P.mix(L.hair, P.INK, 0.4)
        bthick = hw * (0.05 if female else 0.08)
        ang = {"stern": -0.18, "calm": 0.0, "smile": 0.06, "grin": 0.0}.get(L.expr, 0.0)
        self.brow(ex1, ey - eh * 1.25, ew * 0.95, -1, bcol, bthick, ang)
        self.brow(ex2, ey - eh * 1.25, ew * 0.85 * (1 - t * 0.4), 1, bcol, bthick, ang)
        # nose
        nx, ny = hx + t * hw * 0.62, hy + hh * 0.45
        nm = cv.mask(stroke_w([(nx + hw * 0.02, ny - hh * 0.12), (nx + hw * 0.06, ny), (nx - hw * 0.04, ny + hh * 0.04)],
                              [hw * 0.02, hw * 0.045, hw * 0.02]))
        flat_fill(cv, nm, skin_lo(L.skin), 0.9)
        # mouth
        mx, my = hx + t * hw * 0.5, hy + hh * 0.66
        mw = hw * 0.2
        lipc = P.mix(skin_lo(L.skin), P.TERRACOTTA_DK, 0.5)
        if L.expr in ("smile", "grin"):
            pts = [(mx - mw, my - hh * 0.02), (mx, my + hh * 0.05), (mx + mw, my - hh * 0.03)]
        elif L.expr == "stern":
            pts = [(mx - mw, my + hh * 0.01), (mx, my - hh * 0.005), (mx + mw * 0.9, my + hh * 0.015)]
        else:
            pts = [(mx - mw * 0.8, my), (mx, my + hh * 0.02), (mx + mw * 0.8, my)]
        flat_fill(cv, cv.mask(stroke_w(pts, [hw * 0.02, hw * 0.035, hw * 0.02])), lipc, 0.9)
        if L.expr == "grin":
            tm = cv.mask(catmull(A([(mx - mw * 0.8, my - hh * 0.0), (mx + mw * 0.8, my - hh * 0.01), (mx, my + hh * 0.07)]), True, 4))
            flat_fill(cv, tm, P.WHITE_WARM, 0.9)
        # blush
        if L.build in ("f", "child") or L.expr == "smile":
            for bx in (ex1, ex2):
                bm = cv.blur(cv.ellipse_mask(bx + hw * 0.05, ey + eh * 1.6, ew * 0.42, eh * 0.45), hw * 0.06)
                flat_fill(cv, bm, P.BLUSH, 0.35)

    # ------------------------------------------------------------------ hair
    def hair_lock(self, root, tip, width, bend=0.0, col=None, shine=True, key="lk", ctrl=None):
        """One tapered hair lock from root to tip, bending sideways by `bend` (fraction of length)."""
        rx, ry = root
        tx, ty = tip
        dx, dy = tx - rx, ty - ry
        L = math.hypot(dx, dy)
        nx, ny = -dy / max(L, 1e-6), dx / max(L, 1e-6)
        mid = (rx + dx * 0.5 + nx * L * bend, ry + dy * 0.5 + ny * L * bend) if ctrl is None else ctrl
        q = (rx + dx * 0.8 + nx * L * bend * 0.6, ry + dy * 0.8 + ny * L * bend * 0.6)
        return stroke_w([root, mid, q, tip], [width, width * 0.85, width * 0.45, 0.6])

    def paint_hair(self, polys, col=None, key="hair", shine=True, soft=None, line=0.95, shine_y=None, each=True):
        """Paint hair locks (each with its own line so lock separations read), then a shine band."""
        cv = self.cv
        col = col or self.L.hair
        lo = P.mix(P.shadow_of(col, 1.1), P.VIOLET, 0.1)
        lc = P.light_of(col, 1.1)
        allm = np.zeros((cv.PH, cv.PW), F32)
        groups = polys if each else [polys]
        for i, g in enumerate(groups):
            g = g if isinstance(g, list) else [g]
            m = cv.polys_mask(g)
            if m.max() <= 0:
                continue
            drop_shadow(cv, m, 1.2, 2.4, 2.4, 0.22)
            paint(cv, m, col, line=line, line_w=1.0, cel=0.65, ao=0.18, hi=0.35, shade=1.0, lo=lo, light_col=lc,
                  soft=soft, var=0.02, key=key + str(i % 5), line_col=P.mix(col, P.INK, 0.6))
            allm = np.maximum(allm, m)
        if shine and allm.max() > 0:
            hx, hy = self.head_c
            sy = shine_y if shine_y is not None else hy - self.hh * 0.45
            band = np.exp(-((cv.yy() - sy - (cv.xx() - hx) ** 2 * 0.004) / (self.hh * 0.07)) ** 2)
            zig = cv.noise((self.hh * 0.6, self.hw * 0.05), key + "zig", 2)
            band = band * smoothstep(0.35, 0.6, zig)
            cv.atop(P.mix(lc, P.WHITE_WARM, 0.35), np.clip(band * allm, 0, 1) * 0.55)
        return allm

    def hairline_cap(self, fringe=0.0, low=0.0):
        """Base hair cap covering the cranium; returns polygon."""
        hx, hy = self.head_c
        hw, hh = self.hw, self.hh
        return catmull(A([(hx - hw * 1.08, hy + hh * (0.1 + low)), (hx - hw * 1.12, hy - hh * 0.5), (hx - hw * 0.7, hy - hh * 1.05),
                          (hx, hy - hh * 1.16), (hx + hw * 0.75, hy - hh * 1.05), (hx + hw * 1.12, hy - hh * 0.5),
                          (hx + hw * 1.06, hy + hh * (0.1 + low)), (hx + hw * 0.8, hy - hh * (0.25 - fringe)),
                          (hx, hy - hh * (0.45 - fringe)), (hx - hw * 0.8, hy - hh * (0.25 - fringe))]), True, 6)

    # ------------------------------------------------------------------ outfit helpers
    def belt(self, y, w, col, buckle=P.HONEY, tilt=0.0, x0=None, x1=None, buckle_x=None):
        hu = self.hu
        x0 = x0 if x0 is not None else self.cx - self.w_waist * 1.12
        x1 = x1 if x1 is not None else self.cx + self.w_waist * 1.12
        pts = [(x0, y - tilt * hu), (self.cx, y + hu * 0.05), (x1, y + tilt * hu)]
        m = self.band(pts, w, col, shadow=0.28)
        bx = buckle_x if buckle_x is not None else self.cx + self.L.turn * hu * 0.1
        if buckle:
            by = y + hu * 0.04
            self.shape([(bx - w * 0.6, by - w * 0.62), (bx + w * 0.6, by - w * 0.62), (bx + w * 0.6, by + w * 0.62), (bx - w * 0.6, by + w * 0.62)],
                       buckle, smooth=False, shadow=0.25, hi=0.6, gloss=0.4)
            self.shape([(bx - w * 0.3, by - w * 0.3), (bx + w * 0.3, by - w * 0.3), (bx + w * 0.3, by + w * 0.3), (bx - w * 0.3, by + w * 0.3)],
                       col, smooth=False, shadow=0, line=0.4)
        return m

    def trim(self, pts, w, col, motif=None, shadow=0.15):
        m = self.band(pts, w, col, shadow=shadow, cel=0.7, hi=0.5)
        if motif is not None:
            c = catmull(A(pts), closed=False, n=10)
            seg = np.diff(c, axis=0)
            L = np.concatenate([[0], np.cumsum(np.hypot(seg[:, 0], seg[:, 1]))])
            step = w * 1.6
            polys = []
            d = step / 2
            while d < L[-1]:
                i = min(len(c) - 2, np.searchsorted(L, d))
                p = c[i]
                tdir = seg[i] / max(1e-6, np.hypot(*seg[i]))
                n_ = np.array([-tdir[1], tdir[0]])
                s = w * 0.3
                polys.append(A([p + tdir * s, p + n_ * s, p - tdir * s, p - n_ * s]))
                d += step
            mm = self.cv.polys_mask(polys) * m
            self.cv.atop(motif, mm * 0.9)
        return m

    def pauldron(self, side, col, layers=3, size=1.0, trim=None):
        hu = self.hu
        sh = self.shL if side < 0 else self.shR
        x, y = sh[0] + side * hu * 0.08, sh[1] - hu * 0.05
        ms = []
        for k in range(layers - 1, -1, -1):
            rx = hu * 0.36 * size * (1 - k * 0.12)
            ry = hu * 0.22 * size
            yy = y + k * hu * 0.16 * size
            pts = [(x - side * rx * 0.75, yy - ry * 0.5), (x - side * rx * 0.2, yy - ry * 1.15), (x + side * rx * 0.7, yy - ry * 0.85),
                   (x + side * rx * 1.12, yy + ry * 0.2), (x + side * rx * 0.85, yy + ry * 0.75), (x - side * rx * 0.6, yy + ry * 0.55)]
            ms.append(self.shape(pts, col, shadow=0.3, hi=0.55, gloss=0.35, cel=0.7))
            if trim:
                self.trim([(x - side * rx * 0.55, yy + ry * 0.6), (x + side * rx * 0.3, yy + ry * 0.8), (x + side * rx * 0.95, yy + ry * 0.5)],
                          hu * 0.05, trim)
        return ms


# ----------------------------------------------------------------------------------------
# weapons & held items
# ----------------------------------------------------------------------------------------
STEEL = P.mix(P.STONE, P.DUSTY_BLUE, 0.35)
STEEL_DK = P.mix(P.STONE_DK, P.INK, 0.25)
GOLD = P.mix(P.HONEY, P.AMBER, 0.3)
LEATHER = P.hx("7a5236")
LEATHER_DK = P.hx("553826")


def blade(fig, p0, p1, w0, w1=0.0, col=STEEL, edge=True):
    """Straight blade from p0 (guard) to p1 (tip)."""
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    nx, ny = -dy / L, dx / L
    tipb = (x0 + dx * 0.88, y0 + dy * 0.88)
    pts = [(x0 + nx * w0 / 2, y0 + ny * w0 / 2), (tipb[0] + nx * w0 / 2 * 0.9, tipb[1] + ny * w0 / 2 * 0.9), (x1, y1),
           (tipb[0] - nx * w0 / 2 * 0.9, tipb[1] - ny * w0 / 2 * 0.9), (x0 - nx * w0 / 2, y0 - ny * w0 / 2)]
    m = fig.shape(pts, col, smooth=False, shadow=0.25, hi=0.6, gloss=0.5, cel=0.75)
    # lit bevel on one side + fuller line
    half = fig.cv.mask(A([(x0, y0), (tipb[0], tipb[1]), (x1, y1), (tipb[0] + nx * w0 / 2 * 0.9, tipb[1] + ny * w0 / 2 * 0.9),
                          (x0 + nx * w0 / 2, y0 + ny * w0 / 2)])) * m
    fig.cv.atop(P.light_of(col, 1.3), half * 0.55)
    if edge:
        fig.lines([(x0 + dx * 0.04, y0 + dy * 0.04, x0 + dx * 0.75, y0 + dy * 0.75)], max(1.0, w0 * 0.08), P.shadow_of(col, 1.0), 0.6,
                  clip=m)
    return m


def staff(fig, p0, p1, w, col=P.WOOD, tex=True):
    m = fig.band([p0, ((p0[0] + p1[0]) / 2 + 2, (p0[1] + p1[1]) / 2), p1], w, col, shadow=0.25)
    return m


def orb(fig, x, y, r, col, glow_amt=0.7):
    cv = fig.cv
    m = cv.ellipse_mask(x, y, r, r)
    paint(cv, m, col, line=0.9, line_w=1.0, cel=0.4, hi=0.8, gloss=0.6, soft=r * 0.5, light_col=P.mix(col, P.WHITE_WARM, 0.6),
          lo=P.mix(col, P.INK, 0.4))
    core = cv.blur(cv.ellipse_mask(x - r * 0.15, y - r * 0.1, r * 0.45, r * 0.45), r * 0.2) * m
    cv.atop(P.mix(col, P.WHITE_WARM, 0.75), core * glow_amt)
    flat_fill(cv, cv.ellipse_mask(x - r * 0.4, y - r * 0.45, r * 0.18, r * 0.13), P.WHITE_WARM, 0.9)
    return m


# ----------------------------------------------------------------------------------------
# finishing
# ----------------------------------------------------------------------------------------
def finish_char(cv, portrait_box=None, bg=None, ow=2.2):
    """Returns (sprite 512x1024, portrait 256x256 or None)."""
    rim_light(cv, 0.38, 2.6)
    grain(cv, 0.018, cell=1.2, key="paper")
    outline(cv, ow)
    for (x, y, r, col, s) in getattr(cv, "post_glow", []):
        glow(cv, x, y, r, col, s, clip=False, falloff=2.2)
    portrait = None
    if portrait_box is not None:
        portrait = make_portrait(cv, portrait_box, bg)
    rgb, a = downsample(cv.rgb, cv.a, SS)
    return to_image(rgb, a), portrait


def make_portrait(cv, box, bg):
    """box = (cx, cy, size) in design units; bg = (light, dark) colours for the backdrop wash."""
    from PIL import Image
    cx, cy, size = box
    s = cv.s
    x0 = int(round((cx - size / 2) * s))
    y0 = int(round((cy - size / 2) * s))
    n = int(round(size * s))
    rgb = np.zeros((n, n, 3), F32)
    a = np.zeros((n, n), F32)
    sx0, sy0 = max(0, x0), max(0, y0)
    sx1, sy1 = min(cv.PW, x0 + n), min(cv.PH, y0 + n)
    rgb[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = cv.rgb[sy0:sy1, sx0:sx1]
    a[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = cv.a[sy0:sy1, sx0:sx1]
    # backdrop: soft watercolour wash in the character's colour
    bgc = Canvas(256, 256, n / 256.0, seed=cv.seed + "/portrait")
    light, dark = bg if bg is not None else (P.CREAM, P.SAGE)
    yy = bgc.yy() / 256.0
    xx = bgc.xx() / 256.0
    d = np.sqrt((xx - 0.42) ** 2 + (yy - 0.38) ** 2)
    fld = colfield(P.mix(light, P.CREAM, 0.35), P.mix(dark, light, 0.25), np.clip(d * 1.5, 0, 1))
    wash(bgc, np.ones((bgc.PH, bgc.PW), F32), light, 1.0, field=fld, pool=0, gran=0.08, var=0.05, var_cell=60, key="bg")
    blobm = bgc.mask(blob(110, 100, 120, 100, rng(cv.seed, "pb"), 0.2))
    wash(bgc, blobm, P.mix(light, P.WHITE_WARM, 0.5), 0.5, pool=0.4, pool_w=4, gran=0.1, key="bl")
    # composite figure over backdrop
    out_rgb = rgb + bgc.rgb * (1 - a[..., None])
    out_a = np.ones_like(a)
    # soft vignette
    vig = smoothstep(0.45, 0.85, d)
    out_rgb = out_rgb * (1 - 0.18 * vig)[..., None] + np.asarray(P.VIOLET, F32) * (0.06 * vig)[..., None]
    img = to_image(out_rgb, out_a)
    return img.resize((256, 256), Image.LANCZOS)


def portrait_box(fig, scale=1.9, dy=0.32):
    hx, hy = fig.head_c
    size = fig.hu * scale
    return (hx - fig.hu * 0.05, hy + fig.hu * dy, size)


def new_canvas(key):
    return Canvas(512, 1024, SS, seed=key)


# ----------------------------------------------------------------------------------------
# hair styles
# ----------------------------------------------------------------------------------------
def hair_spiky(fig, col=None, back=True, length=1.0, front=True):
    """Spiky heroic hair. back=True paints the back spikes (call before the face), front after."""
    hx, hy = fig.head_c
    hw, hh = fig.hw, fig.hh
    r = rng(fig.cv.seed, "spiky")
    if back:
        locks = [fig.hairline_cap(low=0.25)]
        for k in range(9):
            a = math.pi * (1.05 + k / 8 * 0.9)
            L = hh * (0.75 + 0.35 * r.random()) * length
            root = (hx + math.cos(a) * hw * 0.6, hy - hh * 0.2 + math.sin(a) * hh * 0.6)
            tip = (hx + math.cos(a) * (hw + L), hy - hh * 0.2 + math.sin(a) * (hh + L * 0.8))
            locks.append(fig.hair_lock(root, tip, hw * 0.55, bend=0.12 * (1 if k % 2 else -1)))
        return fig.paint_hair(locks, col, key="hb", shine=False, each=False)
    polys = []
    cap = fig.hairline_cap(fringe=0.0)
    polys.append(cap)
    # bangs falling over the forehead
    for k in range(6):
        t = k / 5
        rx = hx + (t - 0.5) * hw * 1.6
        root = (rx + hw * 0.1, hy - hh * 0.95)
        tip = (hx + (t - 0.5) * hw * 2.0 + hw * 0.15 * (t - 0.5), hy - hh * (0.05 - 0.25 * abs(t - 0.5)) + r.random() * hh * 0.08)
        polys.append(fig.hair_lock(root, tip, hw * 0.48, bend=(0.5 - t) * 0.25))
    # upswept spikes on top
    for k in range(5):
        a = math.pi * (1.2 + k / 4 * 0.6)
        root = (hx + math.cos(a) * hw * 0.3, hy - hh * 0.55)
        tip = (hx + math.cos(a) * hw * 1.25, hy - hh * 0.55 + math.sin(a) * hh * 0.95 * length)
        polys.append(fig.hair_lock(root, tip, hw * 0.5, bend=0.15))
    # side locks
    for side in (-1, 1):
        polys.append(fig.hair_lock((hx + side * hw * 0.8, hy - hh * 0.5), (hx + side * hw * 1.02, hy + hh * 0.42), hw * 0.42,
                                   bend=-side * 0.12))
    return fig.paint_hair(polys, col, key="hf")


# ----------------------------------------------------------------------------------------
# WARRIOR
# ----------------------------------------------------------------------------------------
def stand_collar(f, col, lining, trim=None, height=0.42, flare=1.0):
    """Tall stiff collar standing around the neck, open at the front (inside lining visible)."""
    hu = f.hu
    cx = f.cx
    top = f.chin - hu * (height - 0.25)
    back = [(cx - f.w_neck * 1.25 * flare, top - hu * 0.06), (cx + f.w_neck * 1.25 * flare, top - hu * 0.06),
            (cx + f.w_neck * 1.05, f.sh_y + hu * 0.12), (cx - f.w_neck * 1.05, f.sh_y + hu * 0.12)]
    f.shape(back, lining, smooth=False, shadow=0, line=0.6)
    for s in (-1, 1):
        pts = [(cx + s * f.w_neck * 0.35, f.sh_y + hu * 0.22), (cx + s * f.w_neck * 0.72, top + hu * 0.12),
               (cx + s * f.w_neck * 1.3 * flare, top - hu * 0.07), (cx + s * f.w_neck * 1.5 * flare, top + hu * 0.04),
               (cx + s * f.w_neck * 1.75, f.sh_y + hu * 0.0), (cx + s * f.w_neck * 1.5, f.sh_y + hu * 0.25)]
        f.shape(pts, col if s < 0 else P.scale_v(col, 0.92), n=5)
        if trim:
            f.trim([(cx + s * f.w_neck * 0.72, top + hu * 0.12), (cx + s * f.w_neck * 1.32 * flare, top - hu * 0.07),
                    (cx + s * f.w_neck * 1.55 * flare, top + hu * 0.05)], hu * 0.05, trim)


def long_coat(f, coat, lining, trim, hem_dy=0.55, flare=1.6, motif=True, open_w=0.55, panels=(-1, 1)):
    """Open long coat: back panel must be drawn earlier with coat_back(); this paints the front panels."""
    hu = f.hu
    cx = f.cx
    hem = f.knee_y + hu * hem_dy
    top_y = f.sh_y + hu * 0.05
    for s in panels:
        inner_top = (cx + s * f.w_neck * 0.9, f.neck_y + hu * 0.15)
        inner_mid = (cx + s * f.w_chest * open_w, f.waist_y)
        inner_bot = (cx + s * f.w_hip * (flare * 0.62), hem)
        outer = [(cx + s * f.w_sh * 0.98, top_y + hu * 0.12), (cx + s * f.w_chest * 1.04, f.chest_y + hu * 0.2),
                 (cx + s * f.w_waist * 1.12, f.waist_y), (cx + s * f.w_hip * 1.3, f.hip_y + hu * 0.5),
                 (cx + s * f.w_hip * flare, hem - hu * 0.05)]
        hem_pts = [(cx + s * f.w_hip * (flare * 0.85), hem + hu * 0.06)]
        pts = [inner_top] + outer + hem_pts + [inner_bot, inner_mid]
        if s < 0:
            pts = pts[::-1]
        pm = f.shape(pts, coat, n=6, soft=hu * 0.25)
        segs = []
        for k in range(3):
            x = cx + s * (f.w_hip * (0.95 + k * 0.2))
            segs.append((x, f.hip_y + hu * 0.4, x + s * hu * 0.1, hem - hu * 0.05))
        f.folds(pm, segs, coat, 0.45, hu * 0.03)
        f.trim([inner_top, (cx + s * f.w_chest * (open_w + 0.05), f.chest_y), inner_mid, (cx + s * f.w_hip * 0.8, f.hip_y + hu * 0.5), inner_bot],
               hu * 0.09, trim, motif=P.mix(trim, P.INK, 0.6) if motif else None)
        f.trim([inner_bot, (cx + s * f.w_hip * (flare * 0.85), hem + hu * 0.06), (cx + s * f.w_hip * flare, hem - hu * 0.05)], hu * 0.09, trim)
    return hem


def coat_back(f, col, hem_dy=0.55, flare=1.6):
    hu = f.hu
    cx = f.cx
    hem = f.knee_y + hu * hem_dy
    back = [(cx - f.w_waist * 1.05, f.waist_y), (cx + f.w_waist * 1.05, f.waist_y), (cx + f.w_hip * flare * 0.95, hem),
            (cx, hem + hu * 0.08), (cx - f.w_hip * flare * 0.95, hem)]
    f.shape(back, col, shadow=0)


def draw_warrior(cv, look, pal):
    """Heavy red long coat over dark plate, high collar, greatsword resting on the shoulder."""
    f = Fig(cv, look)
    hu = f.hu
    cx = f.cx
    coat, lining, trim, plate = pal["coat"], pal["lining"], pal["trim"], pal["plate"]
    flare = pal.get("flare", 1.55)
    coat_back(f, P.mix(lining, coat, 0.35), flare=flare)
    # legs & boots with knee guards
    for s in (-1, 1):
        f.leg(s, pal["pants"])
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.knee_y - hu * 0.12, cuff=P.mix(pal["boots"], P.INK, 0.25))
        knee = f.kneeL if s < 0 else f.kneeR
        f.shape([(knee[0] - hu * 0.19, knee[1] - hu * 0.22), (knee[0] + hu * 0.19, knee[1] - hu * 0.22), (knee[0] + hu * 0.15, knee[1] + hu * 0.14),
                 (knee[0], knee[1] + hu * 0.22), (knee[0] - hu * 0.15, knee[1] + hu * 0.14)], plate, hi=0.6, gloss=0.4)
    f.neck()
    # torso: dark under-armour with a high neck + chest plate
    f.torso(pal["under"])
    f.shape([(cx - f.w_neck * 0.75, f.chin - hu * 0.02), (cx + f.w_neck * 0.75, f.chin - hu * 0.02), (cx + f.w_neck * 0.85, f.sh_y + hu * 0.2),
             (cx - f.w_neck * 0.85, f.sh_y + hu * 0.2)], pal["under"], smooth=False, shadow=0)
    cp = [(cx - f.w_chest * 0.8, f.sh_y + hu * 0.32), (cx + f.w_chest * 0.8, f.sh_y + hu * 0.32), (cx + f.w_chest * 0.88, f.chest_y + hu * 0.3),
          (cx + f.w_waist * 0.85, f.waist_y - hu * 0.05), (cx, f.waist_y + hu * 0.1), (cx - f.w_waist * 0.85, f.waist_y - hu * 0.05),
          (cx - f.w_chest * 0.88, f.chest_y + hu * 0.3)]
    m = f.shape(cp, plate, hi=0.6, gloss=0.35, cel=0.7)
    f.lines([(cx, f.sh_y + hu * 0.38, cx, f.waist_y + hu * 0.05)], hu * 0.025, P.light_of(plate, 1.2), 0.7, clip=m)
    f.lines([(cx - f.w_chest * 0.8, f.chest_y + hu * 0.45, cx + f.w_chest * 0.8, f.chest_y + hu * 0.45)], hu * 0.02,
            P.shadow_of(plate, 1.2), 0.6, clip=m)
    hem = long_coat(f, coat, lining, trim, flare=flare)
    if pal.get("mail_skirt"):
        ms = [(cx - f.w_hip * 0.85, f.hip_y), (cx + f.w_hip * 0.85, f.hip_y), (cx + f.w_hip * 0.9, f.knee_y - hu * 0.3),
              (cx - f.w_hip * 0.9, f.knee_y - hu * 0.3)]
        mm = f.shape(ms, pal["mail_skirt"], smooth=False)
        mail_texture(f, mm, pal["mail_skirt"])
    # belts: broad belt + slung hip belt with pouch; asymmetric tasset cloth on the left hip
    tas = [(cx - f.w_waist * 1.05, f.waist_y + hu * 0.12), (cx - f.w_waist * 0.25, f.waist_y + hu * 0.14),
           (cx - f.w_waist * 0.3, f.hip_y + hu * 0.75), (cx - f.w_waist * 0.65, f.hip_y + hu * 0.95), (cx - f.w_waist * 1.0, f.hip_y + hu * 0.7)]
    tm = f.shape(tas, pal["sash"], n=5)
    f.folds(tm, [(cx - f.w_waist * 0.5, f.waist_y + hu * 0.2, cx - f.w_waist * 0.55, f.hip_y + hu * 0.8)], pal["sash"], 0.5, hu * 0.03)
    f.trim([(cx - f.w_waist * 0.3, f.hip_y + hu * 0.75), (cx - f.w_waist * 0.65, f.hip_y + hu * 0.95), (cx - f.w_waist * 1.0, f.hip_y + hu * 0.7)],
           hu * 0.06, trim)
    f.belt(f.waist_y + hu * 0.05, hu * 0.22, pal["belt"], buckle=trim)
    f.band([(cx - f.w_hip * 1.0, f.hip_y - hu * 0.05), (cx, f.hip_y + hu * 0.12), (cx + f.w_hip * 1.05, f.hip_y + hu * 0.3)], hu * 0.1,
           P.mix(pal["belt"], P.INK, 0.2))
    f.shape([(cx + f.w_hip * 0.55, f.hip_y + hu * 0.25), (cx + f.w_hip * 0.92, f.hip_y + hu * 0.3), (cx + f.w_hip * 0.9, f.hip_y + hu * 0.6),
             (cx + f.w_hip * 0.55, f.hip_y + hu * 0.56)], pal["belt"], smooth=False)
    if pal.get("flask"):
        fx, fy = cx + f.w_hip * 0.75, f.hip_y + hu * 0.35
        f.band([(fx - hu * 0.05, fy - hu * 0.32), (fx + hu * 0.02, fy - hu * 0.12)], hu * 0.025, P.CREAM, cap=False)
        f.shape(catmull(A([(fx - hu * 0.13, fy - hu * 0.1), (fx + hu * 0.13, fy - hu * 0.1), (fx + hu * 0.18, fy + hu * 0.2), (fx, fy + hu * 0.32),
                           (fx - hu * 0.18, fy + hu * 0.2)]), True, 5), pal["flask"], smooth=False, hi=0.6, gloss=0.4)
        f.shape([(fx - hu * 0.05, fy - hu * 0.2), (fx + hu * 0.05, fy - hu * 0.2), (fx + hu * 0.05, fy - hu * 0.09), (fx - hu * 0.05, fy - hu * 0.09)],
                P.WOOD_DK, smooth=False, shadow=0.1)
        f.band([(fx - hu * 0.17, fy + hu * 0.08), (fx + hu * 0.17, fy + hu * 0.08)], hu * 0.04, pal["belt"], cap=False)
    if pal.get("tucked"):
        # left arm tucked inside the coat: the empty sleeve hangs loose from the shoulder
        sh = f.shL
        sl = [(sh[0] + hu * 0.05, sh[1] - hu * 0.05), (sh[0] - hu * 0.1, sh[1] + hu * 0.7), (sh[0] - hu * 0.05, sh[1] + hu * 1.35),
              (sh[0] + hu * 0.06, sh[1] + hu * 1.9)]
        sm = f.tube(sl, [f.w_uarm * 1.2, f.w_uarm * 1.0, f.w_farm * 0.85, f.w_farm * 0.75], coat, soft=hu * 0.15)
        f.folds(sm, [(sh[0] - hu * 0.15, sh[1] + hu * 0.4, sh[0] - hu * 0.05, sh[1] + hu * 1.2),
                     (sh[0] + hu * 0.05, sh[1] + hu * 0.9, sh[0] + hu * 0.1, sh[1] + hu * 1.7)], coat, 0.5, hu * 0.035)
        cuff_c = [(sh[0] + hu * 0.06 - f.w_farm * 0.42, sh[1] + hu * 1.78), (sh[0] + hu * 0.06 + f.w_farm * 0.42, sh[1] + hu * 1.82),
                  (sh[0] + hu * 0.08 + f.w_farm * 0.4, sh[1] + hu * 1.98), (sh[0] + hu * 0.04 - f.w_farm * 0.44, sh[1] + hu * 1.95)]
        f.shape(cuff_c, trim, smooth=False, hi=0.5)
        # the hidden hand rests in the coat's opening at the chest
        f.hand(cx - f.w_chest * 0.45, f.chest_y + hu * 0.25, -1.3, fist=True, col=pal["glove"])
    else:
        # left arm hangs relaxed (coat sleeve, gauntlet)
        el = (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.12)
        wr = (f.shL[0] - hu * 0.14, f.shL[1] + hu * 2.1)
        f.arm(-1, el, wr, sleeve=coat, glove=plate, cuff=trim, sleeve_w=1.12)
        f.hand(wr[0], wr[1] + hu * 0.04, 0.1, fist=True, col=pal["glove"])
    # head
    hair_style(f, look.hair_style, back=True)
    stand_collar(f, coat, lining, trim, height=pal.get("collar_h", 0.42))
    f.ears()
    f.face()
    f.features()
    if look.beard:
        beard(f, look.beard, length=getattr(look, "beard_len", 1.0), full=getattr(look, "beard_full", True))
    hair_style(f, look.hair_style, back=False)
    # right arm: hand at the chest gripping the sword which rests on the shoulder; shoulder plate on top
    hand = (cx + f.w_sh * 0.42, f.chest_y + hu * 0.18)
    el = (f.shR[0] + hu * 0.22, f.waist_y - hu * 0.02)
    f.arm(1, el, hand, sleeve=coat, glove=plate, cuff=trim, sleeve_w=1.12)
    f.pauldron(1, plate, layers=3, size=1.05, trim=trim)
    ang = math.radians(29)
    d = (math.sin(ang), -math.cos(ang))
    guard = (hand[0] + d[0] * hu * 0.32, hand[1] + d[1] * hu * 0.32)
    tip = (guard[0] + d[0] * hu * 3.05, guard[1] + d[1] * hu * 3.05)
    bm = blade(f, guard, tip, hu * pal.get("blade_w", 0.44), col=pal.get("blade", STEEL))
    if pal.get("notched"):
        nx_, ny_ = -d[1], d[0]
        for k, (u, s_) in enumerate(((0.35, 1), (0.52, -1), (0.7, 1))):
            px = guard[0] + d[0] * hu * 3.05 * u + nx_ * s_ * hu * pal.get("blade_w", 0.44) * 0.5
            py = guard[1] + d[1] * hu * 3.05 * u + ny_ * s_ * hu * pal.get("blade_w", 0.44) * 0.5
            notch = A([(px + d[0] * hu * 0.07, py + d[1] * hu * 0.07), (px - nx_ * s_ * hu * 0.08, py - ny_ * s_ * hu * 0.08),
                       (px - d[0] * hu * 0.07, py - d[1] * hu * 0.07)])
            nm = cv.mask(notch) * bm
            cv.atop(P.shadow_of(pal.get("blade", STEEL), 1.6), nm.astype(F32) * 0.95)
    pom = (hand[0] - d[0] * hu * 0.36, hand[1] - d[1] * hu * 0.36)
    f.band([pom, guard], hu * 0.1, LEATHER_DK, shadow=0.2)
    f.ell(pom[0], pom[1], hu * 0.075, hu * 0.075, trim, hi=0.6, gloss=0.5)
    gx, gy = guard
    nx, ny = -d[1], d[0]
    f.band([(gx - nx * hu * 0.34, gy - ny * hu * 0.34 + hu * 0.04), (gx, gy), (gx + nx * hu * 0.34, gy + ny * hu * 0.34 + hu * 0.04)], hu * 0.085,
           trim, hi=0.6, gloss=0.4)
    f.ell(gx, gy, hu * 0.07, hu * 0.07, pal.get("gem", P.hx("d9483b")), hi=0.8, gloss=0.6)
    f.hand(hand[0], hand[1] - hu * 0.02, math.radians(-150), fist=True, col=pal["glove"])
    return f


WARRIOR = dict(coat=P.hx("b83a2e"), lining=P.hx("5e1c1c"), trim=GOLD, plate=P.hx("4f5566"), under=P.hx("3a3442"),
               pants=P.hx("3d3846"), boots=LEATHER_DK, belt=LEATHER, sash=P.hx("6e5d5a"), glove=P.hx("3f3a46"))



# ----------------------------------------------------------------------------------------
# generic hair building blocks
# ----------------------------------------------------------------------------------------
def bangs(f, n=6, y_end=0.1, spread=1.0, part=0.0, sweep=0.0, width=0.5, vary=0.12, curl=0.2):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    r = rng(f.cv.seed, "bangs", n)
    out = []
    for k in range(n):
        t = k / max(1, n - 1)
        root = (hx + (t - 0.5) * hw * 1.1 + part * hw, hy - hh * 0.98)
        ye = hy + hh * (y_end - 0.28 * abs(t - 0.5) ** 1.5 + (r.random() - 0.5) * vary)
        tip = (hx + (t - 0.5) * hw * 2.0 * spread + sweep * hw, ye)
        out.append(f.hair_lock(root, tip, hw * width, bend=(0.5 - t) * curl + sweep * 0.15))
    return out


def side_locks(f, y_end, width=0.42, out=0.0, inner=0.0, both=True, n=1):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    res = []
    for side in ((-1, 1) if both else (-1,)):
        for k in range(n):
            root = (hx + side * hw * (0.82 - k * 0.12), hy - hh * 0.6)
            tip = (hx + side * hw * (1.0 + out) - side * inner * hw + side * k * hw * 0.15, y_end - k * hh * 0.2)
            res.append(f.hair_lock(root, tip, hw * width, bend=-side * 0.08))
    return res


def back_mass(f, y_end, width=1.25, n=7, jag=0.1, flare=0.0):
    """Long hair hanging behind the head and shoulders (paint before the body/face)."""
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    r = rng(f.cv.seed, "backmass")
    locks = [f.hairline_cap(low=0.3)]
    for k in range(n):
        t = k / max(1, n - 1)
        root = (hx + (t - 0.5) * hw * 1.4, hy - hh * 0.7)
        tip = (hx + (t - 0.5) * hw * 2 * (width + flare), y_end - r.random() * jag * (y_end - hy))
        ctrl = (hx + (t - 0.5) * hw * 2 * width * 1.05, hy + hh * 0.8)
        locks.append(f.hair_lock(root, tip, hw * 0.75, ctrl=ctrl))
    return locks


def braid(f, pts, w, col, beads=None, key="br"):
    """Braided strand: chain of overlapping ellipses along a path, optional beads."""
    cv = f.cv
    c = catmull(A(pts), closed=False, n=10)
    seg = np.diff(c, axis=0)
    L = np.concatenate([[0], np.cumsum(np.hypot(seg[:, 0], seg[:, 1]))])
    step = w * 0.75
    d = 0
    polys = []
    k = 0
    while d < L[-1]:
        i = min(len(c) - 2, np.searchsorted(L, d))
        p = c[i]
        a = math.atan2(seg[i][1], seg[i][0])
        off = (0.18 if k % 2 else -0.18) * w
        polys.append(ellipse(p[0] - math.sin(a) * off, p[1] + math.cos(a) * off, w * 0.62, w * 0.42, a + (0.5 if k % 2 else -0.5), 14))
        d += step
        k += 1
    f.paint_hair([polys], col, key=key, shine=False, each=True)
    if beads:
        for j, (tt, bc) in enumerate(beads):
            i = min(len(c) - 1, int(tt * (len(c) - 1)))
            p = c[i]
            f.ell(p[0], p[1], w * 0.55, w * 0.62, bc, hi=0.7, gloss=0.6, shadow=0.2)


# ----------------------------------------------------------------------------------------
# outfit building blocks
# ----------------------------------------------------------------------------------------
def skirt(f, top_y, hem_y, w_top, w_hem, col, folds=6, front_split=0.0, scallop=0.0, trim=None, motif=None, shadow=0.25):
    cx = f.cx
    hu = f.hu
    pts = [(cx - w_top, top_y), (cx + w_top, top_y), (cx + w_top * 1.05 + (w_hem - w_top) * 0.35, (top_y + hem_y) / 2),
           (cx + w_hem, hem_y)]
    n = 7
    for k in range(n + 1):
        t = k / n
        x = cx + w_hem - t * 2 * w_hem
        y = hem_y + (hu * scallop * (0.5 + 0.5 * math.cos(t * n * 2 * math.pi)) if scallop else 0) + math.sin(t * math.pi) * hu * 0.08
        pts.append((x, y))
    pts.append((cx - w_top * 1.05 - (w_hem - w_top) * 0.35, (top_y + hem_y) / 2))
    m = f.shape(pts, col, n=5, shadow=shadow, soft=hu * 0.3)
    segs = []
    for k in range(folds):
        t = (k + 0.5) / folds
        xt = cx - w_top + t * 2 * w_top
        xb = cx - w_hem + t * 2 * w_hem
        segs.append((xt, top_y + hu * 0.15, xb, hem_y - hu * 0.02))
    f.folds(m, segs, col, 0.4, hu * 0.035)
    if trim:
        f.trim([(cx - w_hem, hem_y - hu * 0.02), (cx, hem_y + hu * 0.06), (cx + w_hem, hem_y - hu * 0.02)], hu * 0.09, trim, motif=motif)
    if front_split:
        sp = [(cx + front_split * w_top, top_y + hu * 0.3), (cx + front_split * w_hem * 0.9, hem_y + hu * 0.06),
              (cx + front_split * w_hem * 0.5, hem_y + hu * 0.08)]
        f.lines([(sp[0][0], sp[0][1], sp[1][0], sp[1][1])], hu * 0.03, P.shadow_of(col, 1.3), 0.7, clip=m)
    return m


def shorts(f, col, hem_dy=0.3, baggy=1.0, trim=None, motif=None):
    """Shorts/breeches from waist to above/below the knee."""
    hu = f.hu
    cx = f.cx
    m_all = []
    for s in (-1, 1):
        hip = f.hipL if s < 0 else f.hipR
        knee = f.kneeL if s < 0 else f.kneeR
        hem_y = knee[1] - hu * hem_dy
        t = (hem_y - hip[1]) / (knee[1] - hip[1])
        hx_ = hip[0] + (knee[0] - hip[0]) * t
        w = f.w_thigh * (1.15 * baggy)
        pts = [(cx, f.waist_y + hu * 0.1), (cx + s * f.w_waist * 1.05, f.waist_y + hu * 0.1), (cx + s * f.w_hip * 1.05 * baggy, f.hip_y + hu * 0.1),
               (hx_ + s * w * 0.62, hem_y), (hx_ - s * w * 0.55, hem_y + hu * 0.03), (cx + s * hu * 0.02, f.crotch_y + hu * 0.12)]
        m = f.shape(pts, col, n=5)
        f.folds(m, [(hx_ + s * w * 0.1, f.hip_y + hu * 0.3, hx_ + s * w * 0.25, hem_y - hu * 0.05)], col, 0.35, hu * 0.03)
        if trim:
            f.trim([(hx_ - s * w * 0.55, hem_y), (hx_ + s * w * 0.62, hem_y - hu * 0.02)], hu * 0.08, trim, motif=motif)
        m_all.append(m)
    return m_all


def vest(f, col, open_w=0.35, hem_y=None, trim=None, sides=(-1, 1), collar=True):
    hu = f.hu
    cx = f.cx
    hem_y = hem_y or f.waist_y + hu * 0.25
    for s in sides:
        pts = [(cx + s * f.w_neck * 0.8, f.neck_y + hu * 0.1), (cx + s * f.w_sh * 0.92, f.sh_y + hu * 0.1), (cx + s * f.w_sh * 0.95, f.sh_y + hu * 0.4),
               (cx + s * f.w_chest * 0.98, f.chest_y + hu * 0.35), (cx + s * f.w_waist * 1.05, hem_y), (cx + s * f.w_chest * open_w, hem_y + hu * 0.04),
               (cx + s * f.w_chest * open_w, f.chest_y), (cx + s * f.w_neck * 0.9, f.sh_y + hu * 0.25)]
        if s < 0:
            pts = pts[::-1]
        f.shape(pts, col, n=5)
        if trim:
            f.trim([(cx + s * f.w_neck * 0.8, f.neck_y + hu * 0.12), (cx + s * f.w_neck * 0.9, f.sh_y + hu * 0.25),
                    (cx + s * f.w_chest * open_w, f.chest_y), (cx + s * f.w_chest * open_w, hem_y + hu * 0.04)], hu * 0.07, trim)


def headband(f, col, y=-0.45, beads=None, tails=0):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    pts = [(hx - hw * 1.08, hy + hh * (y + 0.15)), (hx, hy + hh * (y - 0.02)), (hx + hw * 1.06, hy + hh * (y + 0.15))]
    f.band(pts, hh * 0.13, col, shadow=0.25, cel=0.7)
    if beads:
        for k, bc in enumerate(beads):
            t = (k + 1) / (len(beads) + 1)
            x = hx - hw + t * 2 * hw
            yy = hy + hh * (y + 0.02 + 0.13 * (2 * t - 1) ** 2)
            f.ell(x, yy, hh * 0.05, hh * 0.05, bc, hi=0.7, gloss=0.5)
    for k in range(tails):
        f.band([(hx - hw * 1.0, hy + hh * (y + 0.15)), (hx - hw * 1.35, hy + hh * (y + 0.35 + k * 0.1)), (hx - hw * 1.55, hy + hh * (y + 0.9 + k * 0.15))],
               hh * 0.1, col, w1=hh * 0.05)


def necklace(f, cols, y_off=0.25, n=9, r=0.05, sag=0.35):
    hu = f.hu
    cx = f.cx
    for k in range(n):
        t = k / (n - 1)
        x = cx + (t - 0.5) * f.w_neck * 2.6
        y = f.neck_y + hu * y_off + math.sin(t * math.pi) * hu * sag * 0.5
        f.ell(x, y, hu * r, hu * r, cols[k % len(cols)], hi=0.7, gloss=0.6, shadow=0.15, line=0.7)


def fur(f, pts, w, col, key="fur"):
    """Fluffy fur trim along a path: chain of soft tufts."""
    cv = f.cv
    c = catmull(A(pts), closed=False, n=8)
    r = rng(cv.seed, key)
    polys = []
    for i in range(0, len(c), 3):
        p = c[i]
        polys.append(blob(p[0], p[1], w * (0.55 + 0.25 * r.random()), w * (0.45 + 0.2 * r.random()), r, 0.25, 7))
    m = cv.polys_mask(polys)
    m = warp(cv, m, w * 0.08, w * 0.6, key + "w")
    drop_shadow(cv, m, 1.5, 3, 3, 0.25)
    paint(cv, m, col, line=0.8, line_w=1.0, cel=0.5, soft=w * 0.35, hi=0.4, ao=0.2, var=0.04, var_cell=w * 0.6)
    # tuft strokes
    segs = []
    for k in range(int(len(c) * 1.2)):
        p = c[r.integers(len(c))]
        a = r.random() * 6.28
        segs.append((p[0], p[1], p[0] + math.cos(a) * w * 0.4, p[1] + math.sin(a) * w * 0.4))
    f.lines(segs, max(0.7, w * 0.05), P.shadow_of(col, 1.0), 0.4, clip=m)
    return m


def sun_emblem(f, cx, cy, r, col, rays=12):
    polys = [ellipse(cx, cy, r * 0.45, r * 0.45, 0, 28)]
    for k in range(rays):
        a = k / rays * 2 * math.pi
        L = r * (1.0 if k % 2 == 0 else 0.75)
        polys.append(A([(cx + math.cos(a - 0.12) * r * 0.42, cy + math.sin(a - 0.12) * r * 0.42), (cx + math.cos(a) * L, cy + math.sin(a) * L),
                        (cx + math.cos(a + 0.12) * r * 0.42, cy + math.sin(a + 0.12) * r * 0.42)]))
    m = f.cv.polys_mask(polys)
    paint(f.cv, m, col, line=0.8, line_w=0.9, cel=0.7, hi=0.7, gloss=0.5, soft=r * 0.2, ao=0)
    return m


def runes(f, mask, col, n=8, s=0.12, key="rn", glow_amt=0.6):
    """Small glowing rune glyphs scattered inside a mask."""
    cv = f.cv
    r = rng(cv.seed, key)
    bb = bbox(mask, 0)
    if bb is None:
        return
    ys, xs = bb
    hu = f.hu
    segs = []
    placed = 0
    tries = 0
    while placed < n and tries < n * 30:
        tries += 1
        py = r.integers(ys.start, ys.stop)
        px = r.integers(xs.start, xs.stop)
        if mask[py, px] < 0.9:
            continue
        x, y = (px - cv.padx) / cv.s, (py - cv.pady) / cv.s
        u = hu * s
        kind = r.integers(4)
        if kind == 0:
            segs += [(x, y - u, x, y + u), (x - u * 0.6, y - u * 0.3, x + u * 0.6, y + u * 0.3)]
        elif kind == 1:
            segs += [(x - u * 0.6, y + u, x, y - u), (x, y - u, x + u * 0.6, y + u), (x - u * 0.3, y + u * 0.1, x + u * 0.3, y + u * 0.1)]
        elif kind == 2:
            segs += [(x - u * 0.5, y - u, x + u * 0.5, y - u), (x, y - u, x, y + u), (x - u * 0.5, y + u * 0.4, x + u * 0.5, y + u * 0.8)]
        else:
            segs += [(x - u * 0.6, y, x, y - u), (x, y - u, x + u * 0.6, y), (x + u * 0.6, y, x, y + u), (x, y + u, x - u * 0.6, y)]
        placed += 1
    m = cv.lines_mask(segs, hu * 0.03) * mask
    gl = cv.blur(m, hu * 0.04) * mask
    cv.atop(col, np.clip(gl * 1.5, 0, 1) * glow_amt * 0.6)
    cv.atop(P.mix(col, P.WHITE_WARM, 0.5), m * 0.95)


# ----------------------------------------------------------------------------------------
# weapons / items
# ----------------------------------------------------------------------------------------
def bow(f, x, y_top, y_bot, col=P.WOOD, grip=LEATHER_DK, bulge=0.32, side=-1):
    hu = f.hu
    L = y_bot - y_top
    ym = (y_top + y_bot) / 2
    pts = [(x + side * hu * 0.12, y_top), (x - side * hu * 0.12, y_top + L * 0.08), (x - side * L * bulge * 0.55, y_top + L * 0.3),
           (x - side * L * bulge * 0.62, ym), (x - side * L * bulge * 0.55, y_bot - L * 0.3), (x - side * hu * 0.12, y_bot - L * 0.08),
           (x + side * hu * 0.12, y_bot)]
    # string
    f.lines([(x + side * hu * 0.1, y_top + hu * 0.02, x + side * hu * 0.1, y_bot - hu * 0.02)], hu * 0.018, P.CREAM, 0.95)
    f.tube(pts, [hu * 0.06, hu * 0.1, hu * 0.13, hu * 0.15, hu * 0.13, hu * 0.1, hu * 0.06], col, hi=0.5)
    gx = x - side * L * bulge * 0.62
    f.band([(gx, ym - hu * 0.22), (gx, ym + hu * 0.22)], hu * 0.17, grip, cap=False)
    for k in (-0.3, 0.3):
        yy = y_top + L * (0.5 + k * 1.55)
        f.ell(x - side * L * bulge * 0.42 * (1 - abs(k) * 0.3), yy, hu * 0.05, hu * 0.05, P.HONEY, hi=0.6)
    return (gx, ym)


def quiver(f, x, y, ang, col=LEATHER, n=5):
    hu = f.hu
    ca, sa = math.cos(ang), math.sin(ang)
    top = (x + sa * hu * 0.9, y - ca * hu * 0.9)
    bot = (x - sa * hu * 0.9, y + ca * hu * 0.9)
    # arrows
    for k in range(n):
        off = (k - (n - 1) / 2) * hu * 0.07
        p0 = (top[0] + ca * off, top[1] + sa * off)
        p1 = (p0[0] + sa * hu * 0.55, p0[1] - ca * hu * 0.55)
        f.band([p0, p1], hu * 0.025, P.WOOD_LIGHT, shadow=0)
        fl = [(p1[0] - ca * hu * 0.06, p1[1] - sa * hu * 0.06), (p1[0] + sa * hu * 0.2, p1[1] - ca * hu * 0.2),
              (p1[0] + ca * hu * 0.06, p1[1] + sa * hu * 0.06)]
        f.shape(fl, [P.WHITE_WARM, P.TERRACOTTA, P.CREAM][k % 3], smooth=False, shadow=0, line=0.7)
    f.band([top, bot], hu * 0.32, col, hi=0.4)
    f.band([(top[0] - sa * hu * 0.02, top[1] + ca * hu * 0.02), (top[0] + sa * hu * 0.08, top[1] - ca * hu * 0.08)], hu * 0.36,
           P.mix(col, P.INK, 0.25), cap=False)


def kite_shield(f, cx, cy, w, h, col, rim, emblem=None):
    pts = [(cx - w / 2, cy - h * 0.42), (cx - w * 0.3, cy - h * 0.5), (cx, cy - h * 0.52), (cx + w * 0.3, cy - h * 0.5),
           (cx + w / 2, cy - h * 0.42), (cx + w * 0.42, cy + h * 0.05), (cx, cy + h * 0.5), (cx - w * 0.42, cy + h * 0.05)]
    outer = f.shape(pts, rim, smooth=True, n=6, shadow=0.35, hi=0.6, gloss=0.4, cel=0.7)
    c = np.asarray(pts) - (cx, cy)
    inner = c * 0.86 + (cx, cy - h * 0.01)
    m = f.shape(inner, col, smooth=True, n=6, shadow=0, hi=0.5, cel=0.7)
    if emblem is not None:
        emblem(cx, cy - h * 0.08, w * 0.24)
    return outer


def hammer(f, hand, head_c, col=STEEL, shaft=P.WOOD, w=1.0):
    hu = f.hu
    hx, hy = head_c
    f.band([hand, (hx, hy)], hu * 0.11, shaft)
    hw, hh = hu * 0.42 * w, hu * 0.26 * w
    head = [(hx - hw, hy - hh), (hx + hw, hy - hh), (hx + hw * 1.05, hy + hh), (hx - hw * 1.05, hy + hh)]
    f.shape(head, col, smooth=False, hi=0.6, gloss=0.4, cel=0.7)
    for s in (-1, 1):
        f.shape([(hx + s * hw, hy - hh * 1.1), (hx + s * hw * 1.18, hy - hh * 1.1), (hx + s * hw * 1.18, hy + hh * 1.1), (hx + s * hw, hy + hh * 1.1)],
                P.mix(col, P.HONEY, 0.6), smooth=False, hi=0.6, gloss=0.4)
    f.band([(hx - hw * 0.15, hy - hh * 0.9), (hx - hw * 0.15, hy + hh * 0.9)], hu * 0.07, P.HONEY, cap=False)


def dagger(f, grip, tip, col=STEEL, hilt=LEATHER_DK, guard=GOLD):
    hu = f.hu
    gx, gy = grip
    dx, dy = tip[0] - gx, tip[1] - gy
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    base = (gx + ux * hu * 0.16, gy + uy * hu * 0.16)
    blade(f, base, tip, hu * 0.13, col=col, edge=False)
    pom = (gx - ux * hu * 0.18, gy - uy * hu * 0.18)
    f.band([pom, base], hu * 0.075, hilt, shadow=0.15)
    f.band([(base[0] - uy * hu * 0.14, base[1] + ux * hu * 0.14), (base[0] + uy * hu * 0.14, base[1] - ux * hu * 0.14)], hu * 0.055, guard,
           hi=0.6)


def spear(f, x, y_top, y_bot, col=P.WOOD, tip=STEEL, tassel=P.VERMILION, feathers=None):
    hu = f.hu
    f.band([(x, y_bot), (x + 1, (y_top + y_bot) / 2), (x + 2, y_top + hu * 0.6)], hu * 0.085, col)
    blade(f, (x + 2, y_top + hu * 0.62), (x + 2, y_top), hu * 0.24, col=tip)
    f.band([(x - hu * 0.07, y_top + hu * 0.64), (x + hu * 0.11, y_top + hu * 0.64)], hu * 0.08, GOLD, cap=False)
    tas = [(x - hu * 0.02, y_top + hu * 0.68), (x + hu * 0.12, y_top + hu * 0.72), (x + hu * 0.2, y_top + hu * 1.2),
           (x + hu * 0.05, y_top + hu * 1.1)]
    f.shape(tas, tassel, n=4)
    if feathers:
        for k, fc in enumerate(feathers):
            fx = x - hu * 0.05 - k * hu * 0.08
            f.shape([(fx, y_top + hu * 0.75), (fx - hu * 0.12, y_top + hu * 1.0), (fx - hu * 0.08, y_top + hu * 1.35), (fx + hu * 0.02, y_top + hu * 1.0)],
                    fc, n=4)


def shakujo(f, x, y_top, y_bot, col=GOLD, shaft=P.WOOD):
    hu = f.hu
    f.band([(x, y_bot), (x, y_top + hu * 0.5)], hu * 0.08, shaft)
    cv = f.cv
    rr = hu * 0.32
    ring = np.clip(cv.ellipse_mask(x, y_top + hu * 0.18, rr, rr * 1.15) - cv.ellipse_mask(x, y_top + hu * 0.18, rr * 0.78, rr * 0.92), 0, 1)
    drop_shadow(cv, ring, 1.5, 3, 3, 0.25)
    paint(cv, ring.astype(F32), col, line=0.9, line_w=0.9, hi=0.7, gloss=0.6, cel=0.7, soft=3)
    f.band([(x, y_top - hu * 0.15), (x, y_top + hu * 0.55)], hu * 0.07, col, hi=0.6)
    for s in (-1, 1):
        for k in range(3):
            yy = y_top + hu * (0.05 + k * 0.17)
            xx = x + s * rr * 0.95
            m = np.clip(cv.ellipse_mask(xx, yy, hu * 0.08, hu * 0.1) - cv.ellipse_mask(xx, yy, hu * 0.05, hu * 0.07), 0, 1)
            paint(cv, m.astype(F32), col, line=0.8, line_w=0.8, hi=0.7, gloss=0.5, cel=0.7, soft=1)
    f.ell(x, y_top + hu * 0.6, hu * 0.09, hu * 0.06, col, hi=0.6, gloss=0.5)
    # streamer
    f.shape([(x + hu * 0.05, y_top + hu * 0.62), (x + hu * 0.2, y_top + hu * 0.7), (x + hu * 0.28, y_top + hu * 1.3), (x + hu * 0.12, y_top + hu * 1.2)],
            P.hx("e86f8a"), n=4)


def grimoire(f, x, y, w, col, page=P.CREAM, glow_col=P.BLIGHT_GLOW):
    hu = f.hu
    h = w * 0.7
    cover = [(x - w * 0.55, y - h * 0.48), (x, y - h * 0.38), (x + w * 0.55, y - h * 0.48), (x + w * 0.56, y + h * 0.5), (x, y + h * 0.58),
             (x - w * 0.56, y + h * 0.5)]
    f.shape(cover, col, smooth=False, hi=0.4)
    pages = [(x - w * 0.5, y - h * 0.5), (x, y - h * 0.36), (x + w * 0.5, y - h * 0.5), (x + w * 0.5, y + h * 0.42), (x, y + h * 0.5),
             (x - w * 0.5, y + h * 0.42)]
    pm = f.shape(pages, page, smooth=False, shadow=0, hi=0.3, line=0.7)
    f.lines([(x, y - h * 0.36, x, y + h * 0.5)], hu * 0.02, P.shadow_of(page, 1.4), 0.7)
    segs = []
    for k in range(5):
        yy = y - h * 0.25 + k * h * 0.13
        segs += [(x - w * 0.42, yy, x - w * 0.08, yy + h * 0.04), (x + w * 0.08, yy + h * 0.04, x + w * 0.42, yy)]
    f.lines(segs, hu * 0.018, P.mix(glow_col, P.VIOLET, 0.5), 0.8, clip=pm)
    f.glow(x, y - h * 0.2, w * 1.0, glow_col, 0.55, clip=False)


def totem_charm(f, x, y, s, col, face=P.INK_SOFT):
    hu = f.hu
    f.lines([(x, y - s * 0.9, x, y - s * 0.45)], hu * 0.015, P.CREAM, 0.9)
    body = [(x - s * 0.22, y - s * 0.5), (x + s * 0.22, y - s * 0.5), (x + s * 0.25, y + s * 0.5), (x - s * 0.25, y + s * 0.5)]
    m = f.shape(body, col, smooth=False, hi=0.5, shadow=0.2, line=0.8)
    f.ell(x - s * 0.09, y - s * 0.18, s * 0.05, s * 0.05, face, line=0, shadow=0)
    f.ell(x + s * 0.09, y - s * 0.18, s * 0.05, s * 0.05, face, line=0, shadow=0)
    f.lines([(x - s * 0.12, y + s * 0.12, x + s * 0.12, y + s * 0.12)], s * 0.05, face, 0.8, clip=m)
    f.shape([(x - s * 0.32, y - s * 0.55), (x + s * 0.32, y - s * 0.55), (x + s * 0.2, y - s * 0.7), (x - s * 0.2, y - s * 0.7)],
            P.mix(col, P.VERMILION, 0.5), smooth=False, shadow=0, line=0.7)


def goggles(f, y_off=-0.62, lens=P.hx("6fd0c4"), frame=P.mix(P.HONEY, P.WOOD, 0.3), third=False):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    y = hy + hh * y_off
    f.band([(hx - hw * 1.08, y + hh * 0.12), (hx, y - hh * 0.04), (hx + hw * 1.06, y + hh * 0.12)], hh * 0.12, LEATHER_DK)
    if third:
        tx = hx + f.L.turn * hw * 0.4 + hw * 0.95
        f.ell(tx, y - hh * 0.22, hw * 0.2, hh * 0.15, frame, hi=0.6, gloss=0.4, shadow=0.25)
        f.ell(tx, y - hh * 0.22, hw * 0.13, hh * 0.1, P.hx("f2b33d"), hi=0.9, gloss=0.8, cel=0.5, shadow=0)
    for s, sc in ((-1, 1.0), (1, 0.9)):
        cx_ = hx + f.L.turn * hw * 0.4 + s * hw * 0.42
        f.ell(cx_, y, hw * 0.33 * sc, hh * 0.24, frame, hi=0.6, gloss=0.4, shadow=0.25)
        m = f.ell(cx_, y, hw * 0.24 * sc, hh * 0.17, lens, hi=0.9, gloss=0.8, cel=0.5, shadow=0)
        flat_fill(f.cv, f.cv.ellipse_mask(cx_ - hw * 0.08, y - hh * 0.06, hw * 0.07, hh * 0.05), P.WHITE_WARM, 0.9)


def scarf(f, col, tail_side=1, tail_len=2.2, trim=None):
    hu = f.hu
    cx = f.cx
    wrap = [(cx - f.w_neck * 1.25, f.chin - hu * 0.05), (cx + f.w_neck * 1.25, f.chin - hu * 0.05), (cx + f.w_neck * 1.6, f.sh_y + hu * 0.12),
            (cx, f.sh_y + hu * 0.28), (cx - f.w_neck * 1.6, f.sh_y + hu * 0.12)]
    m = f.shape(wrap, col, n=5)
    f.folds(m, [(cx - f.w_neck, f.chin + hu * 0.02, cx + f.w_neck, f.chin + hu * 0.12),
                (cx - f.w_neck * 1.2, f.chin + hu * 0.15, cx + f.w_neck * 1.1, f.sh_y + hu * 0.12)], col, 0.5, hu * 0.03)
    s = tail_side
    tail = [(cx + s * f.w_neck * 0.6, f.sh_y + hu * 0.1), (cx + s * f.w_neck * 1.4, f.sh_y + hu * 0.15), (cx + s * f.w_sh * 1.2, f.sh_y + hu * tail_len * 0.5),
            (cx + s * f.w_sh * 1.55, f.sh_y + hu * tail_len), (cx + s * f.w_sh * 1.25, f.sh_y + hu * tail_len * 0.95),
            (cx + s * f.w_sh * 0.95, f.sh_y + hu * tail_len * 0.5)]
    tm = f.shape(tail, P.scale_v(col, 0.95), n=5)
    if trim:
        f.trim([(cx + s * f.w_sh * 1.25, f.sh_y + hu * tail_len * 0.95), (cx + s * f.w_sh * 1.55, f.sh_y + hu * tail_len)], hu * 0.06, trim)
    return m


def cape_back(f, col, hem_dy=0.4, w=1.5):
    hu = f.hu
    cx = f.cx
    hem = f.knee_y + hu * hem_dy
    pts = [(cx - f.w_sh * 0.95, f.sh_y + hu * 0.05), (cx + f.w_sh * 0.95, f.sh_y + hu * 0.05), (cx + f.w_sh * w * 0.75, f.hip_y),
           (cx + f.w_sh * w * 0.85, hem), (cx, hem + hu * 0.1), (cx - f.w_sh * w * 0.85, hem), (cx - f.w_sh * w * 0.75, f.hip_y)]
    m = f.shape(pts, col, n=5, shadow=0, soft=hu * 0.3)
    f.folds(m, [(cx + k * f.w_sh * 0.4, f.hip_y, cx + k * f.w_sh * 0.55, hem) for k in (-1.5, -0.5, 0.5, 1.5)], col, 0.45, hu * 0.04)
    return m


# ----------------------------------------------------------------------------------------
# hair styles (each returns nothing; `back` part is painted before the body, front after the face)
# ----------------------------------------------------------------------------------------
def hair_style(f, style, back):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    L = f.L
    col = L.hair
    hu = f.hu
    if style == "spiky":
        return hair_spiky(f, col, back=back)
    if style == "spiky_band":
        if back:
            return hair_spiky(f, col, back=True, length=0.85)
        hair_spiky(f, col, back=False, length=0.8)
        headband(f, L.band, y=-0.5, tails=2)
        return
    if style == "braids":
        if back:
            f.paint_hair([f.hairline_cap(low=0.2)], col, shine=False)
            for k in range(5):
                t = k / 4
                x0 = hx + (t - 0.5) * hw * 1.6
                braid(f, [(x0, hy - hh * 0.2), (x0 + (t - 0.5) * hw * 0.8, hy + hh * 0.9), (x0 + (t - 0.5) * hw * 1.2, f.sh_y + hu * 0.25)],
                      hw * 0.2, col, beads=[(0.55, P.HONEY), (1.0, P.hx("4fb3a9"))], key="bb%d" % k)
            return
        polys = [f.hairline_cap(fringe=-0.05)]
        for k in range(6):
            t = k / 5
            root = (hx + (t - 0.5) * hw * 1.3, hy - hh * 0.55)
            tip = (hx + (t - 0.5) * hw * 2.3 + hw * 0.35, hy - hh * (1.12 + 0.12 * math.sin(t * math.pi)))
            polys.append(f.hair_lock(root, tip, hw * 0.62, bend=0.25))
        polys += side_locks(f, hy + hh * 0.25, width=0.32)
        f.paint_hair(polys, col)
        headband(f, L.band, y=-0.38, beads=[P.HONEY, P.hx("4fb3a9"), P.HONEY, P.hx("4fb3a9"), P.HONEY])
        return
    if style == "mohawk":
        if back:
            braid(f, [(hx - hw * 0.1, hy - hh * 0.6), (hx - hw * 0.5, hy + hh * 0.6), (hx - hw * 0.7, f.sh_y + hu * 0.6)], hw * 0.24, col,
                  beads=[(0.7, P.TERRACOTTA), (1.0, P.HONEY)], key="mb")
            return
        cap = f.hairline_cap(fringe=-0.15)
        m = f.cv.mask(cap)
        f.cv.atop(P.mix(col, L.skin, 0.55), m * 0.75)
        polys = []
        for k in range(6):
            t = k / 5
            root = (hx - hw * 0.05 + (t - 0.5) * hw * 0.3, hy - hh * (0.75 - 0.05 * t))
            ang = math.pi * (1.35 + t * 0.5)
            tip = (root[0] + math.cos(ang) * hh * 0.9, root[1] + math.sin(ang) * hh * 0.85)
            polys.append(f.hair_lock(root, tip, hw * 0.42, bend=0.1))
        f.paint_hair(polys, col)
        return
    if style == "neat":
        if back:
            return f.paint_hair([f.hairline_cap(low=0.12)], col, shine=False)
        polys = [f.hairline_cap(fringe=-0.08)]
        polys += bangs(f, n=6, y_end=-0.22, spread=0.95, part=-0.2, sweep=0.55, width=0.5, curl=0.35)
        polys += side_locks(f, hy + hh * 0.1, width=0.36)
        f.paint_hair(polys, col)
        return
    if style == "swept_grey":
        if back:
            return f.paint_hair([f.hairline_cap(low=0.2)], col, shine=False)
        polys = [f.hairline_cap(fringe=-0.12)]
        for k in range(5):
            t = k / 4
            root = (hx + (t - 0.5) * hw * 1.2, hy - hh * 0.75)
            tip = (hx + (t - 0.5) * hw * 1.6 + hw * 0.6, hy - hh * 1.05)
            polys.append(f.hair_lock(root, tip, hw * 0.5, bend=0.2))
        polys += side_locks(f, hy + hh * 0.15, width=0.34)
        f.paint_hair(polys, col)
        return
    if style == "bun":
        if back:
            f.paint_hair([f.hairline_cap(low=0.3)], col, shine=False)
            for s in (-1, 1):
                braid(f, [(hx + s * hw * 0.7, hy + hh * 0.3), (hx + s * hw * 1.25, f.sh_y + hu * 0.6), (hx + s * hw * 1.45, f.waist_y)],
                      hw * 0.22, col, beads=[(1.0, L.accent)], key="mbr%d" % s)
            bx, by = hx - hw * 0.15, hy - hh * 0.45
            f.paint_hair([ellipse(bx - hw * 0.62, by, hw * 0.95, hh * 0.78, -0.35, 40)], col, shine=True, shine_y=by - hh * 0.3)
            for s, ang in ((-1, -0.5), (1, 0.4)):
                f.band([(bx - hw * 0.55 - s * hw * 0.9, by - hh * 0.75), (bx - hw * 0.55 + s * hw * 0.9, by + hh * 0.55)], hw * 0.07,
                       P.mix(P.HONEY, P.STONE, 0.3), hi=0.6)
                f.ell(bx - hw * 0.55 - s * hw * 0.92, by - hh * 0.78, hw * 0.1, hw * 0.1, L.accent, hi=0.7, gloss=0.6)
            return
        polys = [f.hairline_cap(fringe=0.05)]
        polys += bangs(f, n=6, y_end=0.05, spread=1.05, part=0.0, sweep=0.15, width=0.45, curl=0.45)
        polys += side_locks(f, hy + hh * 0.75, width=0.4, out=0.05)
        f.paint_hair(polys, col)
        return
    if style == "long":
        if back:
            return f.paint_hair(back_mass(f, L.hair_end if hasattr(L, "hair_end") else f.waist_y, width=1.35, n=8), col, shine=False,
                                each=False)
        polys = [f.hairline_cap(fringe=0.05)]
        polys += bangs(f, n=7, y_end=0.12, spread=1.0, part=0.15, sweep=0.1, width=0.42, curl=0.3)
        polys += side_locks(f, f.chest_y + hu * 0.2, width=0.42, out=0.1, n=2)
        f.paint_hair(polys, col)
        return
    if style == "bob":
        if back:
            f.paint_hair(back_mass(f, f.chin + hu * 0.05, width=1.25, n=7, jag=0.05), col, shine=False, each=False)
            # long wrapped tail down the back (visible beside the body)
            tx = hx + hw * 0.95
            f.band([(tx - hw * 0.4, hy + hh * 0.6), (tx + hw * 0.1, f.sh_y + hu * 0.5), (tx + hw * 0.25, f.hip_y)], hw * 0.22, col, w1=hw * 0.16)
            for k in range(4):
                yy = f.sh_y + hu * (0.6 + k * 0.45)
                f.band([(tx - hw * 0.05, yy), (tx + hw * 0.35, yy + hu * 0.03)], hw * 0.12, L.accent, cap=False)
            return
        polys = [f.hairline_cap(fringe=0.08)]
        polys += bangs(f, n=7, y_end=0.0, spread=1.0, part=0.1, sweep=-0.1, width=0.42, curl=0.3)
        polys += side_locks(f, f.chin + hu * 0.05, width=0.48, out=0.1, inner=0.0)
        polys.append(f.hair_lock((hx - hw * 0.85, hy - hh * 0.3), (hx - hw * 1.05, f.chin + hu * 0.55), hw * 0.3, bend=0.1))
        f.paint_hair(polys, col)
        # bead ornament on the long lock
        f.ell(hx - hw * 1.02, f.chin + hu * 0.35, hw * 0.1, hw * 0.13, L.accent, hi=0.7, gloss=0.5)
        return
    if style == "ponytail":
        if back:
            f.paint_hair([f.hairline_cap(low=0.2)], col, shine=False)
            root = (hx + hw * 0.1, hy - hh * 1.0)
            pts = [root, (hx + hw * 1.2, hy - hh * 1.3), (hx + hw * 1.45, hy + hh * 0.6), (hx + hw * 1.2, f.chest_y + hu * 0.4)]
            f.paint_hair([stroke_w(pts, [hw * 0.5, hw * 0.7, hw * 0.55, 1.0])], col, shine=False)
            return
        polys = [f.hairline_cap(fringe=0.0)]
        polys += bangs(f, n=6, y_end=0.0, spread=1.0, part=-0.1, sweep=0.0, width=0.44, curl=0.3)
        polys += side_locks(f, f.sh_y + hu * 0.2, width=0.34, out=0.05)
        f.paint_hair(polys, col)
        streak = getattr(L, "streak", None)
        if streak is not None:
            f.paint_hair([f.hair_lock((hx - hw * 0.35, hy - hh * 0.95), (hx - hw * 0.62, hy + hh * 0.05), hw * 0.34, bend=0.12),
                          f.hair_lock((hx - hw * 0.82, hy - hh * 0.5), (hx - hw * 1.0, f.sh_y + hu * 0.2), hw * 0.26, bend=-0.08)],
                         streak, key="streak", shine=False)
            for k, bc in enumerate((L.accent, P.HONEY, L.accent)):
                f.ell(hx - hw * 1.02, f.chin + hu * (0.05 + k * 0.12), hw * 0.09, hw * 0.1, bc, hi=0.7, gloss=0.5, shadow=0.15)
        f.band([(hx + hw * 0.0, hy - hh * 1.02), (hx + hw * 0.25, hy - hh * 1.08)], hw * 0.18, L.accent, cap=False)
        return
    if style == "braid_needles":
        if back:
            f.paint_hair(back_mass(f, f.sh_y + hu * 0.3, width=1.2, n=6, jag=0.05), col, shine=False, each=False)
            for s, ang in ((-1, 0.55), (1, -0.45)):
                bx, by = hx + s * hw * 0.25, hy - hh * 0.85
                f.band([(bx - math.cos(ang) * hw * 1.2, by - math.sin(ang) * hw * 1.2 - hh * 0.2),
                        (bx + math.cos(ang) * hw * 0.6, by + math.sin(ang) * hw * 0.6)], hw * 0.06, P.mix(P.STONE, P.HONEY, 0.3), hi=0.6)
                f.ell(bx - math.cos(ang) * hw * 1.22, by - math.sin(ang) * hw * 1.22 - hh * 0.2, hw * 0.09, hw * 0.09, L.accent, hi=0.8, gloss=0.6)
            f.paint_hair([ellipse(hx - hw * 0.1, hy - hh * 0.95, hw * 0.75, hh * 0.38, 0.0, 36)], col, shine=True, shine_y=hy - hh * 1.05)
            return
        polys = [f.hairline_cap(fringe=0.0)]
        polys += bangs(f, n=6, y_end=-0.05, spread=1.0, part=0.25, sweep=0.35, width=0.46, curl=0.3)
        f.paint_hair(polys, col)
        for s in (-1, 1):
            braid(f, [(hx + s * hw * 0.85, hy - hh * 0.3), (hx + s * hw * 1.05, f.chin + hu * 0.2), (hx + s * hw * 0.95, f.chest_y + hu * 0.45)],
                  hw * 0.24, col, beads=[(1.0, L.accent)], key="lb%d" % s)
        return
    if style == "bandana_braids":
        if back:
            f.paint_hair([f.hairline_cap(low=0.2)], col, shine=False)
            for k in range(6):
                t = k / 5
                x0 = hx + (t - 0.5) * hw * 1.8
                braid(f, [(x0, hy - hh * 0.1), (x0 + (t - 0.5) * hw * 0.9, hy + hh * 0.9), (x0 + (t - 0.5) * hw * 1.2, f.sh_y + hu * 0.35)],
                      hw * 0.2, col, beads=[(0.5, P.HONEY), (1.0, [P.hx("4fb3a9"), P.VERMILION, P.HONEY][k % 3])], key="bb%d" % k)
            return
        polys = [f.hairline_cap(fringe=-0.1)]
        polys += side_locks(f, hy + hh * 0.2, width=0.3)
        f.paint_hair(polys, col)
        headscarf(f, L.band, knot=True, pattern=P.hx("f2e6c8"))
        return
    if style == "swept_back":
        if back:
            return f.paint_hair([f.hairline_cap(low=0.22)] + [f.hair_lock((hx + (k - 2) * hw * 0.3, hy - hh * 0.6),
                                                                         (hx + (k - 2) * hw * 0.45 - hw * 0.3, f.chin + hu * 0.05), hw * 0.5,
                                                                         bend=0.1) for k in range(5)], col, shine=False, each=False)
        polys = [f.hairline_cap(fringe=-0.18)]
        r_ = rng(f.cv.seed, "swb")
        for k in range(7):
            t = k / 6
            root = (hx + (t - 0.5) * hw * 1.4, hy - hh * 0.72)
            tip = (hx + (t - 0.5) * hw * 1.9 - hw * 0.45, hy - hh * (0.95 + 0.15 * math.sin(t * math.pi)) + r_.normal() * hh * 0.03)
            polys.append(f.hair_lock(root, tip, hw * 0.46, bend=0.18))
        polys.append(f.hair_lock((hx + hw * 0.2, hy - hh * 0.8), (hx + hw * 0.35, hy - hh * 0.05), hw * 0.16, bend=-0.2))
        polys += side_locks(f, hy + hh * 0.2, width=0.3)
        f.paint_hair(polys, col)
        streak = getattr(L, "streak", None)
        if streak is not None:
            f.paint_hair([f.hair_lock((hx - hw * 0.3, hy - hh * 0.75), (hx - hw * 1.0, hy - hh * 0.75), hw * 0.28, bend=0.2),
                          f.hair_lock((hx + hw * 0.45, hy - hh * 0.7), (hx - hw * 0.1, hy - hh * 1.08), hw * 0.22, bend=0.15),
                          f.hair_lock((hx - hw * 0.85, hy - hh * 0.45), (hx - hw * 1.02, hy + hh * 0.15), hw * 0.18, bend=-0.1)],
                         streak, key="streak", shine=False)
        return
    if style == "rogue":
        if back:
            f.paint_hair([f.hairline_cap(low=0.25)], col, shine=False)
            for k in range(6):
                a = math.pi * (0.95 + k * 0.22)
                x0, y0 = hx + math.cos(a) * hw * 0.7, hy - hh * 0.1 + math.sin(a) * hh * 0.5
                x1, y1 = hx + math.cos(a) * hw * 1.6, hy + hh * 0.9 + k * hh * 0.08
                braid(f, [(x0, y0), ((x0 + x1) / 2 + math.cos(a) * hw * 0.2, (y0 + y1) / 2), (x1, y1)], hw * 0.16, col,
                      beads=[(1.0, [P.hx("4fb3a9"), P.HONEY, P.VERMILION][k % 3])], key="rb%d" % k)
            f.paint_hair([stroke_w([(hx + hw * 0.3, hy - hh * 0.6), (hx + hw * 1.3, hy - hh * 0.2), (hx + hw * 1.4, f.sh_y + hu * 0.8)],
                                   [hw * 0.5, hw * 0.45, 1.0])], col, shine=False)
            return
        polys = [f.hairline_cap(fringe=0.0)]
        r = rng(f.cv.seed, "rogue")
        for k in range(7):
            t = k / 6
            root = (hx + (t - 0.5) * hw * 1.2, hy - hh * 0.95)
            tip = (hx + (t - 0.5) * hw * 2.3 + r.normal() * hw * 0.1, hy + hh * (0.05 - 0.3 * abs(t - 0.5)) + r.normal() * hh * 0.06)
            polys.append(f.hair_lock(root, tip, hw * 0.45, bend=(0.5 - t) * 0.5))
        polys += side_locks(f, f.chin + hu * 0.1, width=0.34, out=0.15)
        f.paint_hair(polys, col)
        goggles(f)
        return
    if style == "twin_buns":
        if back:
            f.paint_hair([f.hairline_cap(low=0.15)], col, shine=False)
            for s in (-1, 1):
                f.paint_hair([ellipse(hx + s * hw * 0.85, hy - hh * 0.85, hw * 0.42, hw * 0.42, 0, 30)], col, shine=True,
                             shine_y=hy - hh * 1.0)
                f.band([(hx + s * hw * 0.55, hy - hh * 0.6), (hx + s * hw * 1.05, hy - hh * 0.55)], hw * 0.12, L.accent, cap=False)
            return
        polys = [f.hairline_cap(fringe=0.05)]
        polys += bangs(f, n=6, y_end=-0.02, spread=1.05, part=0.0, sweep=0.0, width=0.45, curl=0.25)
        polys += side_locks(f, f.chin - hu * 0.05, width=0.36, out=0.05)
        f.paint_hair(polys, col)
        if getattr(L, "goggles", False):
            goggles(f, y_off=-0.66, third=True, lens=P.hx("7fd8e8"))
        return
    if style == "swept_long":
        if back:
            return f.paint_hair(back_mass(f, f.chest_y + hu * 0.5, width=1.25, n=7), col, shine=False, each=False)
        polys = [f.hairline_cap(fringe=-0.1)]
        for k in range(5):
            t = k / 4
            root = (hx + (t - 0.5) * hw * 1.2, hy - hh * 0.7)
            ang = math.pi * (1.25 + 0.5 * t)
            tip = (root[0] + math.cos(ang) * hh * (0.9 + 0.5 * math.sin(t * math.pi)), root[1] + math.sin(ang) * hh * (1.2 + 0.4 * math.sin(t * math.pi)))
            polys.append(f.hair_lock(root, tip, hw * 0.5, bend=0.18 * (1 if t > 0.5 else -1)))
        polys += side_locks(f, f.chest_y + hu * 0.1, width=0.38, out=0.15, n=2)
        f.paint_hair(polys, col)
        return
    if style == "bald_beard":
        if back:
            return
        m = f.cv.mask(catmull(A([(hx - hw * 1.1, hy + hh * 0.2), (hx - hw * 1.08, hy - hh * 0.35), (hx - hw * 0.75, hy - hh * 0.3),
                                  (hx - hw * 0.85, hy + hh * 0.15)]), True, 4))
        polys = [f.hair_lock((hx - hw * 0.85, hy - hh * 0.35), (hx - hw * 1.1, hy + hh * 0.3), hw * 0.28, bend=0.1),
                 f.hair_lock((hx - hw * 0.7, hy - hh * 0.45), (hx - hw * 1.18, hy + hh * 0.05), hw * 0.22, bend=0.15),
                 f.hair_lock((hx + hw * 0.8, hy - hh * 0.35), (hx + hw * 1.05, hy + hh * 0.3), hw * 0.26, bend=-0.1),
                 f.hair_lock((hx + hw * 0.65, hy - hh * 0.45), (hx + hw * 1.12, hy + hh * 0.05), hw * 0.2, bend=-0.15)]
        f.paint_hair(polys, col, shine=False)
        return
    if style == "short":
        if back:
            return f.paint_hair([f.hairline_cap(low=0.1)], col, shine=False)
        polys = [f.hairline_cap(fringe=-0.05)]
        polys += bangs(f, n=5, y_end=-0.15, spread=0.95, part=0.1, sweep=0.2, width=0.5, curl=0.3)
        polys += side_locks(f, hy + hh * 0.15, width=0.34)
        f.paint_hair(polys, col)
        return
    if style == "none":
        return


def beard(f, col, length=1.0, mustache=True, full=True):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    t = f.L.turn
    if full:
        pts = [(hx - hw * 0.95, hy + hh * 0.1), (hx - hw * 0.7, hy + hh * 0.65), (hx - hw * 0.45, hy + hh * (1.1 + 0.6 * length)),
               (hx + t * hw * 0.3, hy + hh * (1.3 + 0.9 * length)), (hx + hw * 0.5, hy + hh * (1.1 + 0.6 * length)), (hx + hw * 0.75, hy + hh * 0.6),
               (hx + hw * 0.92, hy + hh * 0.1), (hx + hw * 0.5, hy + hh * 0.55), (hx + t * hw * 0.4, hy + hh * 0.78), (hx - hw * 0.45, hy + hh * 0.55)]
        f.paint_hair([catmull(A(pts), True, 6)], col, shine=False, key="beard")
        segs = []
        r = rng(f.cv.seed, "beardstr")
        for k in range(12):
            x = hx + (r.random() - 0.5) * hw * 1.2
            segs.append((x, hy + hh * 0.75, x + (x - hx) * 0.2, hy + hh * (1.0 + 0.8 * length * r.random())))
        f.lines(segs, hw * 0.025, P.shadow_of(col, 1.0), 0.4)
    if mustache:
        mx, my = hx + t * hw * 0.5, hy + hh * 0.58
        for s in (-1, 1):
            f.paint_hair([f.hair_lock((mx, my - hh * 0.02), (mx + s * hw * 0.42, my + hh * 0.2), hw * 0.16, bend=s * 0.1)], col, shine=False,
                         key="mus")


# ----------------------------------------------------------------------------------------
# heads
# ----------------------------------------------------------------------------------------
def human_head(f, back_only=False, front_extra=None):
    L = f.L
    if back_only:
        hair_style(f, L.hair_style, back=True)
        return
    f.ears()
    f.face()
    if L.build == "old":
        hx, hy = f.head_c
        f.lines([(hx - f.hw * 0.75, hy + f.hh * 0.42, hx - f.hw * 0.6, hy + f.hh * 0.55),
                 (hx + f.hw * 0.62, hy + f.hh * 0.42, hx + f.hw * 0.5, hy + f.hh * 0.55),
                 (hx - f.hw * 0.25, hy - f.hh * 0.38, hx + f.hw * 0.3, hy - f.hh * 0.4)], f.hw * 0.025, skin_lo(L.skin), 0.6)
    f.features()
    if L.beard:
        beard(f, L.beard, length=getattr(L, "beard_len", 1.0))
    hair_style(f, L.hair_style, back=False)
    if front_extra:
        front_extra()


def beast_head(f, fur_col, muzzle, horn, mane, eyes):
    """Ram-horned feline beast-folk head (front, slight 3/4)."""
    cv = f.cv
    hx, hy = f.head_c
    hy = hy + f.hh * 0.05
    hw, hh = f.hw * 1.32, f.hh * 1.15
    t = f.L.turn
    r = rng(cv.seed, "beast")
    # mane tufts behind the head (radiating, jagged)
    tufts = []
    for k in range(13):
        a = math.pi * (0.85 + k / 12 * 1.3)
        root = (hx + math.cos(a) * hw * 0.5, hy - hh * 0.1 + math.sin(a) * hh * 0.5)
        L = 1.15 + 0.2 * r.random()
        tip = (hx + math.cos(a) * hw * L * 1.05, hy - hh * 0.1 + math.sin(a) * hh * L)
        tufts.append(f.hair_lock(root, tip, hw * 0.5, bend=0.18))
    f.paint_hair(tufts, P.mix(mane, P.LAVENDER, 0.25), key="mane", shine=False)
    # cheek tufts + head
    pts = [(hx - hw * 0.2, hy - hh * 1.0), (hx + hw * 0.65, hy - hh * 0.85), (hx + hw * 1.0, hy - hh * 0.3), (hx + hw * 1.25, hy + hh * 0.15),
           (hx + hw * 0.98, hy + hh * 0.25), (hx + hw * 1.15, hy + hh * 0.5), (hx + hw * 0.7, hy + hh * 0.6), (hx + hw * 0.4, hy + hh * 0.95),
           (hx + t * hw * 0.3, hy + hh * 1.02), (hx - hw * 0.4, hy + hh * 0.95), (hx - hw * 0.75, hy + hh * 0.6), (hx - hw * 1.2, hy + hh * 0.52),
           (hx - hw * 1.0, hy + hh * 0.25), (hx - hw * 1.3, hy + hh * 0.12), (hx - hw * 1.02, hy - hh * 0.3), (hx - hw * 0.7, hy - hh * 0.85)]
    m = f.shape(pts, fur_col, n=4, shadow=0, soft=hw * 0.3, hi=0.35, cel=0.6)
    mz = [(hx + t * hw * 0.35 - hw * 0.42, hy + hh * 0.38), (hx + t * hw * 0.35, hy + hh * 0.22), (hx + t * hw * 0.35 + hw * 0.42, hy + hh * 0.38),
          (hx + t * hw * 0.35 + hw * 0.35, hy + hh * 0.82), (hx + t * hw * 0.35, hy + hh * 0.98), (hx + t * hw * 0.35 - hw * 0.35, hy + hh * 0.82)]
    f.shape(mz, muzzle, n=5, shadow=0, line=0.5, soft=hw * 0.2)
    nx, ny = hx + t * hw * 0.38, hy + hh * 0.42
    f.shape([(nx - hw * 0.17, ny - hh * 0.05), (nx + hw * 0.17, ny - hh * 0.05), (nx + hw * 0.04, ny + hh * 0.1), (nx - hw * 0.04, ny + hh * 0.1)],
            P.mix(P.INK, fur_col, 0.25), n=4, shadow=0, hi=0.6, gloss=0.4)
    lc = P.mix(P.INK, fur_col, 0.2)
    f.lines([(nx, ny + hh * 0.1, nx, ny + hh * 0.25), (nx, ny + hh * 0.25, nx - hw * 0.2, ny + hh * 0.33), (nx, ny + hh * 0.25, nx + hw * 0.2, ny + hh * 0.33)],
            hw * 0.03, lc, 0.85)
    # fur strokes
    segs = []
    for k in range(26):
        a = r.random() * 2 * math.pi
        d = 0.5 + 0.45 * r.random()
        x, y = hx + math.cos(a) * hw * d, hy + math.sin(a) * hh * d * 0.9
        segs.append((x, y, x + math.cos(a) * hw * 0.15, y + math.sin(a) * hh * 0.15))
    f.lines(segs, hw * 0.02, P.shadow_of(fur_col, 1.0), 0.35, clip=m)
    # eyes: feline, slit pupils, heavy brow
    ey = hy + hh * 0.02
    for d, ex, sc in ((-1, hx + t * hw * 0.4 - hw * 0.48, 1.0), (1, hx + t * hw * 0.4 + hw * 0.45, 1 - t * 0.5)):
        w, h = hw * 0.36 * sc, hw * 0.2
        em = cv.mask(catmull(A([(ex - d * w * 0.5, ey + h * 0.1), (ex, ey - h * 0.5), (ex + d * w * 0.5, ey - h * 0.15), (ex, ey + h * 0.45)]), True, 5))
        paint(cv, em, eyes, line=0.0, soft=2, ao=0, flat=True)
        cv.atop(P.light_of(eyes, 1.4), em * smoothstep(ey - h * 0.2, ey + h * 0.5, cv.yy()) * 0.6)
        flat_fill(cv, cv.ellipse_mask(ex, ey, w * 0.07, h * 0.42) * em, P.INK, 0.95)
        flat_fill(cv, cv.ellipse_mask(ex - w * 0.15, ey - h * 0.15, w * 0.08, h * 0.12) * em, P.WHITE_WARM, 0.9)
        lid = [(ex + d * w * 0.6, ey - h * 0.25), (ex, ey - h * 0.6), (ex - d * w * 0.55, ey + h * 0.05)]
        flat_fill(cv, cv.mask(stroke_w(lid, [h * 0.15, h * 0.32, h * 0.12])), P.INK, 1.0)
        brow = [(ex + d * w * 0.65, ey - h * 0.85), (ex, ey - h * 1.15), (ex - d * w * 0.6, ey - h * 0.75)]
        f.paint_hair([stroke_w(brow, [h * 0.3, h * 0.55, h * 0.25])], mane, shine=False, key="brow")
    # mouth line with small fangs
    my = hy + hh * 0.72
    f.lines([(nx - hw * 0.22, my, nx + hw * 0.24, my - hh * 0.01)], hw * 0.025, lc, 0.8)
    for s in (-1, 1):
        f.shape([(nx + s * hw * 0.13, my), (nx + s * hw * 0.09, my), (nx + s * hw * 0.11, my + hh * 0.08)], P.WHITE_WARM, smooth=False,
                shadow=0, line=0.6)
    # ram horns curling back and around the ears
    for s in (-1, 1):
        cx_, cy_ = hx + s * hw * 1.08, hy - hh * 0.05
        pts = []
        ws = []
        for k in range(10):
            u = k / 9
            th = math.radians(-115 + u * 330) if s > 0 else math.radians(-65 - u * 330)
            rad = hw * (0.82 - 0.55 * u)
            pts.append((cx_ + math.cos(th) * rad * 0.95, cy_ + math.sin(th) * rad))
            ws.append(hw * (0.42 - 0.3 * u))
        hm = cv.mask(stroke_w(pts, ws))
        drop_shadow(cv, hm, 2, 4, 4, 0.3)
        paint(cv, hm, horn, line=0.95, line_w=1.0, cel=0.65, hi=0.5, soft=hw * 0.12, ao=0.15)
        c = catmull(A(pts), closed=False, n=8)
        rid = []
        for k in range(2, len(c) - 2, 3):
            seg = c[k + 1] - c[k - 1]
            n_ = np.array([-seg[1], seg[0]]) / max(1e-6, np.hypot(*seg))
            w_ = ws[min(len(ws) - 1, k // 8)] * 0.5
            rid.append((c[k][0] - n_[0] * w_, c[k][1] - n_[1] * w_, c[k][0] + n_[0] * w_, c[k][1] + n_[1] * w_))
        f.lines(rid, hw * 0.025, P.shadow_of(horn, 1.2), 0.6, clip=hm)
        if getattr(f.L, "horn_charms", False):
            px, py = pts[2]
            cord = [(px, py), (px + s * hw * 0.08, py + hh * 0.35), (px + s * hw * 0.05, py + hh * 0.7)]
            f.band(cord, hw * 0.05, P.hx("c9783a"), shadow=0.15)
            for k, bc in enumerate((P.HONEY, P.hx("4fb3a9"), P.CREAM)):
                f.ell(px + s * hw * (0.08 - k * 0.01), py + hh * (0.25 + k * 0.17), hw * 0.07, hw * 0.08, bc, hi=0.7, gloss=0.5, shadow=0.1)
            f.shape([(px + s * hw * 0.05, py + hh * 0.7), (px + s * hw * 0.16, py + hh * 0.75), (px + s * hw * 0.12, py + hh * 1.05),
                     (px + s * hw * 0.0, py + hh * 1.0)], P.WHITE_WARM, n=4, shadow=0.1)
    if getattr(f.L, "markings", None) is not None:
        mk = f.L.markings
        for s in (-1, 1):
            cx_ = hx + t * hw * 0.35 + s * hw * 0.62
            f.lines([(cx_ - s * hw * 0.05, hy + hh * 0.3, cx_ + s * hw * 0.22, hy + hh * 0.36),
                     (cx_ - s * hw * 0.02, hy + hh * 0.42, cx_ + s * hw * 0.2, hy + hh * 0.5)], hw * 0.05, mk, 0.85, clip=m)
        f.lines([(hx + t * hw * 0.35, hy - hh * 0.55, hx + t * hw * 0.35, hy - hh * 0.25)], hw * 0.06, mk, 0.85, clip=m)
    return m


# ----------------------------------------------------------------------------------------
# HUNTER
# ----------------------------------------------------------------------------------------
def draw_hunter(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    quiver(f, f.shR[0] - hu * 0.2, f.sh_y + hu * 0.55, math.radians(28), pal["leather"])
    human_head(f, back_only=True)
    for s in (-1, 1):
        f.leg(s, None, bare=True)
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.ankle_y - hu * 0.45, cuff=pal["fur"])
        knee = f.kneeL if s < 0 else f.kneeR
        ank = f.ankL if s < 0 else f.ankR
        for k in range(4):
            t = 0.3 + k * 0.14
            x = knee[0] + (ank[0] - knee[0]) * t
            y = knee[1] + (ank[1] - knee[1]) * t
            f.band([(x - hu * 0.2, y - hu * 0.04), (x + hu * 0.2, y + hu * 0.04)], hu * 0.07, pal["wrap"], cap=False)
    shorts(f, pal["shorts"], hem_dy=-0.12, baggy=1.3, trim=pal["trim"], motif=pal["leather"])
    f.neck()
    f.torso(None, bare=True)
    f.lines([(cx - f.w_chest * 0.5, f.chest_y + hu * 0.25, cx - hu * 0.05, f.chest_y + hu * 0.32),
             (cx + hu * 0.05, f.chest_y + hu * 0.32, cx + f.w_chest * 0.5, f.chest_y + hu * 0.25),
             (cx, f.chest_y + hu * 0.5, cx, f.waist_y)], hu * 0.025, skin_lo(look.skin), 0.55)
    vest(f, pal["vest"], open_w=0.45, hem_y=f.waist_y + hu * 0.18, trim=pal["trim"])
    # fur pelt over the left shoulder, strap across the chest
    fur(f, [(cx - f.w_neck * 1.2, f.sh_y + hu * 0.05), (cx - f.w_sh * 0.8, f.sh_y + hu * 0.0), (cx - f.w_sh * 1.05, f.sh_y + hu * 0.35),
            (cx - f.w_sh * 0.9, f.chest_y + hu * 0.1)], hu * 0.3, pal["fur"], key="pelt")
    f.band([(cx - f.w_sh * 0.75, f.sh_y + hu * 0.15), (cx, f.chest_y + hu * 0.3), (cx + f.w_waist * 1.0, f.waist_y + hu * 0.05)], hu * 0.1,
           pal["leather"])
    f.belt(f.waist_y + hu * 0.12, hu * 0.2, pal["sash"], buckle=None)
    kx, ky = cx + f.w_waist * 0.85, f.waist_y + hu * 0.15
    f.ell(kx, ky, hu * 0.14, hu * 0.12, pal["sash"])
    f.shape([(kx - hu * 0.05, ky + hu * 0.05), (kx + hu * 0.1, ky + hu * 0.05), (kx + hu * 0.18, ky + hu * 0.75), (kx + hu * 0.02, ky + hu * 0.7)],
            pal["sash"], n=4)
    necklace(f, [P.HONEY, P.hx("4fb3a9"), P.CREAM], y_off=0.18, n=9)
    if pal.get("number"):
        # sporty "7" stitched on the left vest panel
        nx_, ny_ = cx - f.w_chest * 0.72, f.chest_y + hu * 0.15
        s_ = hu * 0.22
        f.lines([(nx_ - s_ * 0.5, ny_ - s_ * 0.6, nx_ + s_ * 0.5, ny_ - s_ * 0.6), (nx_ + s_ * 0.5, ny_ - s_ * 0.6, nx_ - s_ * 0.15, ny_ + s_ * 0.8)],
                hu * 0.07, pal["number"], 0.95)
    if pal.get("ringball"):
        bx, by = cx + f.w_hip * 1.25, f.hip_y + hu * 0.45
        bm = f.ell(bx, by, hu * 0.3, hu * 0.3, pal["ringball"], shadow=0.3, hi=0.6, gloss=0.4)
        f.lines([(bx - hu * 0.3, by, bx + hu * 0.3, by), (bx, by - hu * 0.3, bx, by + hu * 0.3)], hu * 0.035, P.mix(pal["ringball"], P.INK, 0.5), 0.8,
                clip=bm)
        f.cv.atop(P.WHITE_WARM, np.clip(f.cv.ellipse_mask(bx, by, hu * 0.3, hu * 0.3) - f.cv.ellipse_mask(bx, by, hu * 0.24, hu * 0.24), 0, 1) * 0.0)
        f.band([(bx - hu * 0.28, by - hu * 0.22), (bx - hu * 0.05, f.hip_y + hu * 0.02)], hu * 0.04, pal["leather"], cap=False)
    # left arm holds the bow at the side
    el = (f.shL[0] - hu * 0.35, f.shL[1] + hu * 1.05)
    wr = (f.shL[0] - hu * 0.52, f.hip_y + hu * 0.0)
    f.arm(-1, el, wr, glove=pal["leather"], cuff=pal["trim"])
    bow(f, wr[0] + hu * 0.05, f.sh_y - hu * 0.45, f.knee_y + hu * 0.25, col=pal["bow"], side=-1, bulge=0.12)
    f.hand(wr[0], wr[1] + hu * 0.02, 0.0, fist=True)
    # right arm relaxed, arrow in hand
    el = (f.shR[0] + hu * 0.22, f.shR[1] + hu * 1.1)
    wr = (f.shR[0] + hu * 0.3, f.shR[1] + hu * 2.05)
    f.arm(1, el, wr, glove=pal["leather"], cuff=pal["trim"])
    f.band([(wr[0] - hu * 0.15, wr[1] - hu * 0.3), (wr[0] + hu * 0.25, wr[1] + hu * 0.75)], hu * 0.03, P.WOOD_LIGHT, shadow=0.1)
    f.hand(wr[0], wr[1] + hu * 0.02, -0.2, fist=True)
    human_head(f)
    return f


# ----------------------------------------------------------------------------------------
# PALADIN
# ----------------------------------------------------------------------------------------
def mail_texture(f, mask, col):
    cv = f.cv
    yy = cv.yy()
    xx = cv.xx()
    s = f.hu * 0.06
    pat = (np.sin(xx / s * math.pi + (np.floor(yy / s) % 2) * math.pi) * np.sin(yy / s * math.pi)) ** 2
    cv.atop(P.shadow_of(col, 1.0), (mask * smoothstep(0.6, 0.95, pat) * 0.35).astype(F32))


def draw_paladin(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    cape_back(f, pal["cape"], hem_dy=0.5, w=1.45)
    human_head(f, back_only=True)
    for s in (-1, 1):
        m = f.leg(s, pal["mail"])
        mail_texture(f, m, pal["mail"])
    for s in (-1, 1):
        f.boot(s, pal["plate"], top_y=f.knee_y - hu * 0.25, cuff=pal["trim"])
    f.neck()
    m = f.torso(pal["mail"])
    mail_texture(f, m, pal["mail"])
    # faulds (hip plates) at the sides
    for s in (-1, 1):
        for k in range(pal.get("faulds", 3)):
            y = f.waist_y + hu * (0.15 + k * 0.22)
            f.shape([(cx + s * f.w_waist * 0.6, y), (cx + s * f.w_hip * 1.2, y + hu * 0.05), (cx + s * f.w_hip * 1.25, y + hu * 0.25),
                     (cx + s * f.w_waist * 0.65, y + hu * 0.22)], pal["plate"], smooth=False, hi=0.6, gloss=0.4)
    cp = [(cx - f.w_chest * 0.95, f.sh_y + hu * 0.25), (cx + f.w_chest * 0.95, f.sh_y + hu * 0.25), (cx + f.w_chest, f.chest_y + hu * 0.3),
          (cx + f.w_waist * 0.95, f.waist_y), (cx, f.waist_y + hu * 0.12), (cx - f.w_waist * 0.95, f.waist_y), (cx - f.w_chest, f.chest_y + hu * 0.3)]
    f.shape(cp, pal["plate"], hi=0.65, gloss=0.45, cel=0.72)
    # tabard: long front panel with emblem
    asym = pal.get("asym", 0.0)
    tb = [(cx - f.w_chest * 0.6, f.sh_y + hu * 0.4), (cx + f.w_chest * 0.6, f.sh_y + hu * 0.4), (cx + f.w_waist * 0.75, f.waist_y),
          (cx + f.w_hip * 0.75, f.knee_y + hu * (0.25 - asym)), (cx, f.knee_y + hu * (0.4 - asym * 0.3)),
          (cx - f.w_hip * 0.75, f.knee_y + hu * (0.25 + asym * 0.6)), (cx - f.w_waist * 0.75, f.waist_y)]
    if asym:
        # one-shouldered tabard: strap over the left shoulder only
        tb[0] = (cx - f.w_sh * 0.85, f.sh_y + hu * 0.1)
        tb[1] = (cx + f.w_chest * 0.35, f.sh_y + hu * 0.5)
    tm = f.shape(tb, pal["tabard"], n=6)
    f.folds(tm, [(cx - hu * 0.15, f.hip_y, cx - hu * 0.2, f.knee_y), (cx + hu * 0.2, f.hip_y + hu * 0.2, cx + hu * 0.25, f.knee_y)], pal["tabard"],
            0.4, hu * 0.03)
    if asym:
        f.trim([tb[0], tb[6], tb[5], tb[4], tb[3], tb[2], tb[1]], hu * 0.08, pal["accent"], motif=pal["trim"])
    else:
        f.trim([(cx - f.w_chest * 0.6, f.sh_y + hu * 0.42), (cx - f.w_waist * 0.75, f.waist_y), (cx - f.w_hip * 0.75, f.knee_y + hu * 0.25),
                (cx, f.knee_y + hu * 0.4), (cx + f.w_hip * 0.75, f.knee_y + hu * 0.25), (cx + f.w_waist * 0.75, f.waist_y),
                (cx + f.w_chest * 0.6, f.sh_y + hu * 0.42)], hu * 0.08, pal["accent"], motif=pal["trim"])
    sun_emblem(f, cx + look.turn * hu * 0.1, f.chest_y + hu * 0.12, hu * 0.36, pal["trim"])
    f.belt(f.waist_y + hu * 0.08, hu * 0.2, pal["belt"], buckle=pal["trim"])
    # gorget
    f.shape([(cx - f.w_neck * 1.3, f.chin - hu * 0.02), (cx + f.w_neck * 1.3, f.chin - hu * 0.02), (cx + f.w_neck * 1.7, f.sh_y + hu * 0.25),
             (cx, f.sh_y + hu * 0.35), (cx - f.w_neck * 1.7, f.sh_y + hu * 0.25)], pal["plate"], hi=0.6, gloss=0.4)
    # right arm: hand on the warhammer whose head rests on the ground
    el = (f.shR[0] + hu * 0.3, f.shR[1] + hu * 1.05)
    hand = (f.shR[0] + hu * 0.45, f.hip_y + hu * 0.05)
    if pal.get("bare_right"):
        f.arm(1, el, hand, sleeve=None, glove=LEATHER, cuff=pal["trim"])
        f.band([(f.shR[0] + hu * 0.05, f.shR[1] + hu * 0.45), (f.shR[0] + hu * 0.28, f.shR[1] + hu * 0.5)], hu * 0.07, pal["accent"], cap=False)
        hammer(f, (hand[0] + hu * 0.02, hand[1] - hu * 0.35), (hand[0] + hu * 0.18, f.feet - hu * 0.3), col=pal["plate"])
        f.hand(hand[0], hand[1] - hu * 0.05, 0.0, fist=True, col=LEATHER)
    else:
        f.arm(1, el, hand, sleeve=pal["mail"], glove=pal["plate"], cuff=pal["trim"])
        hammer(f, (hand[0] + hu * 0.02, hand[1] - hu * 0.35), (hand[0] + hu * 0.18, f.feet - hu * 0.3), col=pal["plate"])
        f.hand(hand[0], hand[1] - hu * 0.05, 0.0, fist=True, col=pal["plate"])
        f.pauldron(1, pal["plate"], layers=3, size=1.2, trim=pal["trim"])
    # left arm bent behind the kite shield
    el = (f.shL[0] - hu * 0.25, f.shL[1] + hu * 1.0)
    wr = (cx - f.w_waist * 0.4, f.waist_y + hu * 0.25)
    f.arm(-1, el, wr, sleeve=pal["mail"], glove=pal["plate"])
    f.pauldron(-1, pal["plate"], layers=pal.get("pauldron_layers", 3), size=1.2 if not pal.get("bare_right") else 1.0, trim=pal["trim"])
    human_head(f)
    kite_shield(f, cx - f.w_sh * 0.72, f.waist_y + hu * 0.55, hu * 1.25, hu * 2.0, pal["shield"], pal["plate"],
                emblem=lambda x, y, r: sun_emblem(f, x, y, r, pal["trim"]))
    return f


# ----------------------------------------------------------------------------------------
# MAGE
# ----------------------------------------------------------------------------------------
def draw_mage(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    human_head(f, back_only=True)
    # staff (behind the right hand)
    sx = f.shR[0] + hu * 0.55
    staff(f, (sx, f.feet - hu * 0.05), (sx - hu * 0.05, f.top - hu * 0.05), hu * 0.1, col=pal["staff"])
    curl = [(sx - hu * 0.05, f.top + hu * 0.1), (sx - hu * 0.35, f.top - hu * 0.15), (sx - hu * 0.2, f.top - hu * 0.55), (sx + hu * 0.25, f.top - hu * 0.55),
            (sx + hu * 0.35, f.top - hu * 0.2), (sx + hu * 0.1, f.top + hu * 0.0)]
    f.band(curl, hu * 0.08, pal["staff"])
    orb(f, sx + hu * 0.0, f.top - hu * 0.25, hu * 0.22, pal["orb"])
    f.glow(sx, f.top - hu * 0.25, hu * 0.9, pal["orb"], 0.4)
    # long belted skirt to the floor: overlapping belt strips fanning out, silver buckles
    hem = f.feet + hu * 0.02
    m = skirt(f, f.waist_y + hu * 0.05, hem, f.w_hip * 0.95, f.w_hip * 2.4, pal["strip"], folds=0, scallop=0.12)
    n = 13
    tones = [pal["dress"], pal["strip"], P.mix(pal["dress"], pal["belt"], 0.5)]
    order = sorted(range(n), key=lambda k: abs(k - (n - 1) / 2), reverse=True)
    for k in order:
        t = (k + 0.5) / n
        xt = cx - f.w_hip * 0.85 + t * f.w_hip * 1.7
        xb = cx - f.w_hip * 2.3 + t * f.w_hip * 4.6
        col = tones[k % 3]
        wb = hu * (0.26 if k % 3 else 0.32)
        f.tube([(xt, f.hip_y - hu * 0.05), ((xt * 0.55 + xb * 0.45), (f.hip_y + hem) / 2), (xb, hem + hu * 0.04)], [wb * 0.8, wb, wb * 1.25],
               col, cap=False, shadow=0.35, cel=0.7, hi=0.4)
        for j, u in enumerate((0.28, 0.6)):
            if (k + j) % 2:
                continue
            bx = xt + (xb - xt) * u * u * 0.9 + (xb - xt) * u * 0.1
            by = f.hip_y + (hem - f.hip_y) * u
            f.shape([(bx - hu * 0.09, by - hu * 0.06), (bx + hu * 0.09, by - hu * 0.06), (bx + hu * 0.09, by + hu * 0.06), (bx - hu * 0.09, by + hu * 0.06)],
                    pal["metal"], smooth=False, shadow=0.2, hi=0.7, gloss=0.4, line=0.8)
            f.shape([(bx - hu * 0.045, by - hu * 0.025), (bx + hu * 0.045, by - hu * 0.025), (bx + hu * 0.045, by + hu * 0.025),
                     (bx - hu * 0.045, by + hu * 0.025)], col, smooth=False, shadow=0, line=0.5)
    f.neck()
    # bodice
    f.torso(pal["dress"], neckline=0.65)
    f.shape([(cx - f.w_chest * 0.8, f.chest_y - hu * 0.05), (cx, f.chest_y + hu * 0.1), (cx + f.w_chest * 0.8, f.chest_y - hu * 0.05),
             (cx + f.w_waist * 0.9, f.waist_y + hu * 0.1), (cx, f.waist_y + hu * 0.25), (cx - f.w_waist * 0.9, f.waist_y + hu * 0.1)],
            pal["strip"], hi=0.35)
    for k in range(3):
        y = f.chest_y + hu * (0.25 + k * 0.2)
        f.lines([(cx - f.w_waist * 0.7, y, cx + f.w_waist * 0.7, y + hu * 0.02)], hu * 0.03, pal["metal"], 0.8)
    # crossing hip belts
    f.band([(cx - f.w_hip * 1.05, f.waist_y + hu * 0.1), (cx, f.hip_y + hu * 0.05), (cx + f.w_hip * 1.05, f.hip_y + hu * 0.25)], hu * 0.12,
           pal["belt"])
    f.band([(cx - f.w_hip * 1.05, f.hip_y + hu * 0.3), (cx, f.hip_y + hu * 0.1), (cx + f.w_hip * 1.05, f.waist_y + hu * 0.12)], hu * 0.12,
           P.mix(pal["belt"], P.INK, 0.2))
    f.ell(cx, f.hip_y + hu * 0.08, hu * 0.1, hu * 0.08, pal["metal"], hi=0.7, gloss=0.5)
    # fur stole around the shoulders
    fur(f, [(cx - f.w_sh * 1.05, f.sh_y + hu * 0.45), (cx - f.w_sh * 0.85, f.sh_y + hu * 0.02), (cx - f.w_neck * 1.4, f.sh_y + hu * 0.05),
            (cx - f.w_neck * 0.9, f.chest_y - hu * 0.05), (cx, f.chest_y + hu * 0.05), (cx + f.w_neck * 0.9, f.chest_y - hu * 0.05),
            (cx + f.w_neck * 1.4, f.sh_y + hu * 0.05), (cx + f.w_sh * 0.85, f.sh_y + hu * 0.02), (cx + f.w_sh * 1.05, f.sh_y + hu * 0.45)],
        hu * 0.36, pal["fur"], key="fc1")
    f.ell(cx, f.chest_y + hu * 0.05, hu * 0.1, hu * 0.12, pal["orb"], hi=0.8, gloss=0.6, shadow=0.2)
    # arms in long sleeves, bell cuffs with fur
    el = (f.shL[0] - hu * 0.1, f.shL[1] + hu * 1.1)
    wr = (cx - f.w_waist * 0.6, f.waist_y + hu * 0.35)
    f.arm(-1, el, wr, sleeve=pal["dress"], sleeve_w=1.2)
    f.shape([(wr[0] - hu * 0.32, wr[1] - hu * 0.35), (wr[0] + hu * 0.05, wr[1] - hu * 0.38), (wr[0] + hu * 0.25, wr[1] + hu * 0.25),
             (wr[0] - hu * 0.45, wr[1] + hu * 0.25)], pal["dress"], n=4)
    fur(f, [(wr[0] - hu * 0.42, wr[1] + hu * 0.2), (wr[0] + hu * 0.22, wr[1] + hu * 0.2)], hu * 0.11, pal["fur"], key="cf1")
    f.hand(wr[0] - hu * 0.05, wr[1] + hu * 0.25, 0.4, fist=False)
    hand = (sx - hu * 0.02, f.waist_y + hu * 0.1)
    el = (f.shR[0] + hu * 0.25, f.shR[1] + hu * 1.05)
    f.arm(1, el, hand, sleeve=pal["dress"], sleeve_w=1.2)
    fur(f, [(hand[0] - hu * 0.22, hand[1] - hu * 0.15), (hand[0] + hu * 0.22, hand[1] - hu * 0.1)], hu * 0.11, pal["fur"], key="cf2")
    f.hand(hand[0], hand[1] - hu * 0.08, 0.0, fist=True)
    human_head(f)
    return f


# ----------------------------------------------------------------------------------------
# PRIEST
# ----------------------------------------------------------------------------------------
def floral(f, mask, cols, n=10, s=0.09, key="fl"):
    cv = f.cv
    r = rng(cv.seed, key)
    bb = bbox(mask)
    if bb is None:
        return
    ys, xs = bb
    hu = f.hu
    placed = 0
    tries = 0
    while placed < n and tries < 400:
        tries += 1
        py, px = r.integers(ys.start, ys.stop), r.integers(xs.start, xs.stop)
        if mask[py, px] < 0.95:
            continue
        x, y = (px - cv.padx) / cv.s, (py - cv.pady) / cv.s
        rr = hu * s * (0.7 + 0.6 * r.random())
        c = cols[placed % len(cols)]
        pet = [ellipse(x + math.cos(k * 1.257) * rr * 0.55, y + math.sin(k * 1.257) * rr * 0.55, rr * 0.5, rr * 0.36, k * 1.257, 10) for k in range(5)]
        pm = cv.polys_mask(pet) * mask
        paint(cv, pm.astype(F32), c, line=0.7, line_w=0.6, soft=1.5, ao=0, hi=0.4, cel=0.7)
        flat_fill(cv, cv.ellipse_mask(x, y, rr * 0.2, rr * 0.2) * mask, P.HONEY, 0.9)
        placed += 1


def draw_priest(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    # staff with rings (behind the left hand)
    sx = f.shL[0] - hu * 0.55
    shakujo(f, sx, f.top - hu * 0.35, f.feet - hu * 0.02, col=pal["gold"])
    human_head(f, back_only=True)
    # obi bow loops behind the waist
    for s in (-1, 1):
        f.shape([(cx + s * f.w_waist * 0.6, f.waist_y), (cx + s * f.w_hip * 1.6, f.waist_y - hu * 0.35), (cx + s * f.w_hip * 1.9, f.waist_y + hu * 0.1),
                 (cx + s * f.w_hip * 1.55, f.waist_y + hu * 0.3), (cx + s * f.w_waist * 0.7, f.waist_y + hu * 0.15)], pal["obi"], n=5)
    f.shape([(cx + f.w_waist * 0.4, f.waist_y + hu * 0.1), (cx + f.w_hip * 1.35, f.hip_y + hu * 0.4), (cx + f.w_hip * 1.25, f.knee_y + hu * 0.1),
             (cx + f.w_hip * 0.95, f.knee_y), (cx + f.w_hip * 0.9, f.hip_y + hu * 0.3)], P.scale_v(pal["obi"], 0.9), n=5)
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.knee_y + hu * 0.0, cuff=pal["gold"])
    # long pleated hakama-like skirt
    skirt(f, f.waist_y + hu * 0.05, f.ankle_y - hu * 0.2, f.w_hip * 1.0, f.w_hip * 1.65, pal["skirt"], folds=7, front_split=-0.25,
          trim=pal["trim"], motif=pal["gold"])
    f.neck()
    # kimono top with crossed collar
    t = f.torso(pal["top"])
    f.shape([(cx - f.w_neck * 0.9, f.neck_y + hu * 0.0), (cx - f.w_neck * 0.2, f.neck_y), (cx + f.w_chest * 0.5, f.waist_y),
             (cx + f.w_chest * 0.2, f.waist_y + hu * 0.05)], pal["trim"], smooth=False, shadow=0.2)
    f.shape([(cx + f.w_neck * 0.9, f.neck_y + hu * 0.0), (cx + f.w_neck * 0.3, f.neck_y), (cx - f.w_chest * 0.15, f.chest_y + hu * 0.25),
             (cx + f.w_neck * 0.1, f.chest_y + hu * 0.35)], P.scale_v(pal["trim"], 0.9), smooth=False, shadow=0.2)
    floral(f, t, [pal["flower1"], pal["flower2"]], n=6, s=0.11, key="fl_top")
    # obi
    f.shape([(cx - f.w_waist * 1.08, f.waist_y - hu * 0.15), (cx + f.w_waist * 1.08, f.waist_y - hu * 0.15), (cx + f.w_waist * 1.1, f.waist_y + hu * 0.2),
             (cx - f.w_waist * 1.1, f.waist_y + hu * 0.2)], pal["obi"], smooth=False, hi=0.4)
    f.band([(cx - f.w_waist * 1.08, f.waist_y + hu * 0.02), (cx + f.w_waist * 1.08, f.waist_y + hu * 0.04)], hu * 0.05, pal["cord"], cap=False)
    f.ell(cx + f.w_waist * 0.2, f.waist_y + hu * 0.03, hu * 0.07, hu * 0.06, pal["cord"], hi=0.6)
    necklace(f, [pal["gold"], pal["flower2"]], y_off=0.3, n=7, r=0.04, sag=0.5)
    # right hand raised to the chest (prayer gesture); detached wide sleeves with floral hems
    for s in (-1, 1):
        sh = f.shL if s < 0 else f.shR
        if s > 0:
            el = (sh[0] + hu * 0.2, sh[1] + hu * 1.1)
            wr = (cx + f.w_chest * 0.35, f.chest_y + hu * 0.35)
        else:
            el = (sh[0] - hu * 0.3, sh[1] + hu * 1.05)
            wr = (sx + hu * 0.12, f.waist_y + hu * 0.15)
        f.arm(s, el, wr, sleeve=pal["top"], bare_from=0.75, sleeve_w=1.0)
        sl = [(sh[0] - s * hu * 0.1, sh[1] + hu * 0.05), (sh[0] + s * hu * 0.35, sh[1] + hu * 0.1), (el[0] + s * hu * 0.55, el[1] + hu * 0.6),
              (el[0] + s * hu * 0.35, el[1] + hu * 1.35), (el[0] - s * hu * 0.3, el[1] + hu * 1.3), (el[0] - s * hu * 0.25, el[1] + hu * 0.2)]
        sm = f.shape(sl, pal["top"], n=5)
        hemb = cv.mask(A([(el[0] - s * hu * 0.6, el[1] + hu * 0.95), (el[0] + s * hu * 0.9, el[1] + hu * 0.95),
                          (el[0] + s * hu * 0.9, el[1] + hu * 1.6), (el[0] - s * hu * 0.6, el[1] + hu * 1.6)])) * sm
        cv.atop(pal["skirt"], hemb * 0.85)
        floral(f, hemb.astype(F32), [pal["flower1"], pal["flower2"], P.WHITE_WARM], n=5, s=0.1, key="fs%d" % s)
        f.trim([(el[0] + s * hu * 0.35, el[1] + hu * 1.35), (el[0] - s * hu * 0.3, el[1] + hu * 1.3)], hu * 0.06, pal["gold"])
        f.hand(wr[0], wr[1], -0.6 if s > 0 else 0.0, fist=s < 0)
    human_head(f)
    return f


# ----------------------------------------------------------------------------------------
# ROGUE
# ----------------------------------------------------------------------------------------
def draw_rogue(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    # playful stance: right knee bent outward
    f.kneeR = (f.kneeR[0] + hu * 0.18, f.kneeR[1] - hu * 0.05)
    f.ankR = (f.ankR[0] + hu * 0.2, f.ankR[1])
    human_head(f, back_only=True)
    scarf_tail = [(cx - f.w_neck * 0.5, f.sh_y + hu * 0.1), (cx - f.w_sh * 1.3, f.sh_y + hu * 0.1), (cx - f.w_sh * 1.75, f.sh_y + hu * 0.6),
                  (cx - f.w_sh * 1.95, f.chest_y + hu * 0.3), (cx - f.w_sh * 1.55, f.chest_y + hu * 0.1), (cx - f.w_sh * 1.2, f.sh_y + hu * 0.55)]
    f.shape(scarf_tail, P.scale_v(pal["scarf"], 0.9), n=5)
    for s in (-1, 1):
        f.leg(s, None, bare=True)
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.knee_y + hu * 0.12, cuff=pal["accent"])
        knee = f.kneeL if s < 0 else f.kneeR
        ank = f.ankL if s < 0 else f.ankR
        for k in range(4):
            u = 0.4 + k * 0.13
            x = knee[0] + (ank[0] - knee[0]) * u
            y = knee[1] + (ank[1] - knee[1]) * u
            f.lines([(x - hu * 0.13, y, x + hu * 0.13, y + hu * 0.08), (x - hu * 0.13, y + hu * 0.08, x + hu * 0.13, y)], hu * 0.025, P.CREAM, 0.8)
    # thigh strap with pouch (asymmetric)
    hipL, kneeL = f.hipL, f.kneeL
    ty = f.crotch_y + hu * 0.5
    tx = hipL[0] + (kneeL[0] - hipL[0]) * 0.45
    f.band([(tx - hu * 0.3, ty - hu * 0.02), (tx + hu * 0.28, ty + hu * 0.04)], hu * 0.09, pal["belt"], cap=False)
    f.shape([(tx - hu * 0.3, ty + hu * 0.0), (tx - hu * 0.05, ty + hu * 0.02), (tx - hu * 0.06, ty + hu * 0.3), (tx - hu * 0.3, ty + hu * 0.28)],
            pal["belt"], smooth=False)
    shorts(f, pal["shorts"], hem_dy=1.35, baggy=1.05, trim=pal["accent"])
    f.neck()
    f.torso(None, bare=True, bottom=f.hip_y)
    top = [(cx - f.w_neck * 0.6, f.neck_y + hu * 0.12), (cx - f.w_sh * 0.85, f.sh_y + hu * 0.2), (cx - f.w_chest * 1.0, f.chest_y + hu * 0.1),
           (cx - f.w_chest * 0.9, f.chest_y + hu * 0.55), (cx, f.chest_y + hu * 0.62), (cx + f.w_chest * 0.9, f.chest_y + hu * 0.55),
           (cx + f.w_chest * 1.0, f.chest_y + hu * 0.1), (cx + f.w_sh * 0.85, f.sh_y + hu * 0.2), (cx + f.w_neck * 0.6, f.neck_y + hu * 0.12)]
    tm = f.shape(top, pal["top"], n=5)
    f.trim([(cx - f.w_chest * 0.9, f.chest_y + hu * 0.55), (cx, f.chest_y + hu * 0.62), (cx + f.w_chest * 0.9, f.chest_y + hu * 0.55)], hu * 0.07,
           pal["accent"])
    f.belt(f.waist_y + hu * 0.25, hu * 0.14, pal["belt"], buckle=pal["metal"], tilt=0.12)
    for k, px in enumerate((cx - f.w_hip * 0.85, cx + f.w_hip * 0.7)):
        py = f.waist_y + hu * (0.32 + k * 0.12)
        f.shape([(px - hu * 0.15, py), (px + hu * 0.15, py), (px + hu * 0.14, py + hu * 0.3), (px - hu * 0.14, py + hu * 0.3)], pal["belt"], smooth=False)
        f.shape([(px - hu * 0.16, py - hu * 0.02), (px + hu * 0.16, py - hu * 0.02), (px + hu * 0.14, py + hu * 0.1), (px - hu * 0.14, py + hu * 0.1)],
                P.mix(pal["belt"], P.INK, 0.2), smooth=False, shadow=0)
    scarf(f, pal["scarf"], tail_side=1, tail_len=1.6)
    # arms: left low with dagger pointing down, right raised with reverse-grip dagger
    el = (f.shL[0] - hu * 0.32, f.shL[1] + hu * 1.0)
    wr = (f.shL[0] - hu * 0.42, f.hip_y + hu * 0.05)
    f.arm(-1, el, wr, glove=pal["glove"], cuff=pal["accent"], sleeve=pal.get("sleeve_l"), bare_from=0.7 if pal.get("sleeve_l") else 0.0)
    for k in range(2):
        y = f.shL[1] + hu * (0.45 + k * 0.12)
        f.band([(f.shL[0] - hu * 0.32, y), (f.shL[0] - hu * 0.02, y + hu * 0.03)], hu * 0.05, pal["accent"], cap=False)
    dagger(f, (wr[0] - hu * 0.02, wr[1] + hu * 0.12), (wr[0] - hu * 0.25, wr[1] + hu * 1.05))
    f.hand(wr[0], wr[1] + hu * 0.04, 0.2, fist=True, col=pal["glove"])
    el = (f.shR[0] + hu * 0.5, f.shR[1] + hu * 0.75)
    wr = (f.shR[0] + hu * 0.25, f.chest_y + hu * 0.25)
    f.arm(1, el, wr, glove=pal["glove"], cuff=pal["accent"], sleeve=pal.get("sleeve_r"), bare_from=0.55 if pal.get("sleeve_r") else 0.0)
    dagger(f, (wr[0] + hu * 0.02, wr[1] + hu * 0.0), (wr[0] + hu * 0.6, wr[1] + hu * 0.7))
    f.hand(wr[0], wr[1] - hu * 0.05, -2.6, fist=True, col=pal["glove"])
    human_head(f)
    return f


# ----------------------------------------------------------------------------------------
# WARLOCK
# ----------------------------------------------------------------------------------------
def draw_warlock(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    coat, trim = pal["coat"], pal["trim"]
    human_head(f, back_only=True)
    coat_back(f, P.mix(pal["lining"], coat, 0.3), hem_dy=1.6, flare=1.45)
    for s in (-1, 1):
        f.leg(s, pal["pants"])
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.knee_y + hu * 0.4, toe=1.4)
    f.neck()
    f.torso(pal["vest"])
    for k in range(4):
        y = f.chest_y + hu * (0.1 + k * 0.25)
        f.ell(cx + hu * 0.05, y, hu * 0.045, hu * 0.045, trim, hi=0.7, gloss=0.5, shadow=0.15)
    f.band([(cx - f.w_waist * 1.05, f.waist_y + hu * 0.05), (cx, f.waist_y + hu * 0.12), (cx + f.w_waist * 1.05, f.waist_y + hu * 0.05)], hu * 0.18,
           pal["sash"])
    hem = long_coat(f, coat, pal["lining"], trim, hem_dy=1.6, flare=1.45, open_w=0.45)
    # glowing runes on the coat panels
    for s in (-1, 1):
        reg = cv.mask(A([(cx + s * f.w_hip * 0.85, f.hip_y + hu * 0.3), (cx + s * f.w_hip * 1.4, f.hip_y + hu * 0.3),
                         (cx + s * f.w_hip * 1.4, hem - hu * 0.25), (cx + s * f.w_hip * 0.9, hem - hu * 0.25)]))
        runes(f, (reg * (cv.a > 0.99)).astype(F32), pal["rune"], n=5, s=0.11, key="rn%d" % s)
    # shoulder ornaments
    for s in (-1, 1):
        sh = f.shL if s < 0 else f.shR
        f.shape([(sh[0] - s * hu * 0.25, sh[1] - hu * 0.15), (sh[0] + s * hu * 0.35, sh[1] - hu * 0.2), (sh[0] + s * hu * 0.5, sh[1] + hu * 0.15),
                 (sh[0] + s * hu * 0.1, sh[1] + hu * 0.25)], coat, hi=0.4)
        f.trim([(sh[0] - s * hu * 0.2, sh[1] + hu * 0.2), (sh[0] + s * hu * 0.48, sh[1] + hu * 0.12)], hu * 0.07, trim)
    # left hand: open grimoire at the chest; right hand raised with a violet flame
    gx, gy = cx - f.w_chest * 0.55, f.waist_y - hu * 0.05
    el = (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.05)
    f.arm(-1, el, (gx - hu * 0.05, gy + hu * 0.15), sleeve=coat, sleeve_w=1.15, glove=pal["glove"], cuff=trim)
    grimoire(f, gx, gy, hu * 0.85, pal["book"], glow_col=pal["rune"])
    f.hand(gx - hu * 0.1, gy + hu * 0.25, -0.4, fist=False, col=pal["glove"])
    hand = (f.shR[0] + hu * 0.55, f.chest_y - hu * 0.1)
    el = (f.shR[0] + hu * 0.5, f.waist_y - hu * 0.1)
    f.arm(1, el, hand, sleeve=coat, sleeve_w=1.15, glove=pal["glove"], cuff=trim)
    f.hand(hand[0], hand[1], math.pi, fist=False, col=pal["glove"])
    fl = cv.mask(catmull(A([(hand[0] - hu * 0.2, hand[1] - hu * 0.2), (hand[0] - hu * 0.05, hand[1] - hu * 0.75), (hand[0] + hu * 0.05, hand[1] - hu * 0.55),
                            (hand[0] + hu * 0.18, hand[1] - hu * 0.85), (hand[0] + hu * 0.25, hand[1] - hu * 0.25)]), True, 5))
    flat_fill(cv, fl, pal["rune"], 0.85)
    flat_fill(cv, cv.blur(fl, hu * 0.06) * 0.0 + cv.ellipse_mask(hand[0] + hu * 0.03, hand[1] - hu * 0.32, hu * 0.08, hu * 0.16), P.WHITE_WARM, 0.8)
    f.glow(hand[0], hand[1] - hu * 0.4, hu * 0.8, pal["rune"], 0.35)
    stand_collar(f, coat, pal["lining"], trim, height=0.75, flare=1.5)
    human_head(f)
    if pal.get("imp"):
        tiny_imp(f, f.shL[0] - hu * 0.05, f.sh_y - hu * 0.02, hu * 0.55)
    return f


def tiny_imp(f, x, y, s):
    """A tiny imp familiar perched on a shoulder (s = its height)."""
    skin, dk = P.hx("e0603a"), P.hx("9a3326")
    f.tube([(x - s * 0.1, y - s * 0.25), (x - s * 0.45, y - s * 0.1), (x - s * 0.55, y - s * 0.4)], [s * 0.07, s * 0.05, s * 0.02], dk, shadow=0.1)
    for sd in (-1, 1):
        f.shape([(x + sd * s * 0.1, y - s * 0.55), (x + sd * s * 0.5, y - s * 0.85), (x + sd * s * 0.45, y - s * 0.5)], P.mix(dk, P.VIOLET, 0.3),
                smooth=False, shadow=0.1)
    f.shape(ellipse(x, y - s * 0.35, s * 0.2, s * 0.24, 0, 20), skin, smooth=False, shadow=0.2)
    for sd in (-1, 1):
        f.tube([(x + sd * s * 0.1, y - s * 0.2), (x + sd * s * 0.14, y + s * 0.02)], [s * 0.08, s * 0.06], skin, shadow=0.1)
    hx, hy = x + s * 0.02, y - s * 0.7
    for sd in (-1, 1):
        f.shape([(hx + sd * s * 0.15, hy - s * 0.02), (hx + sd * s * 0.42, hy - s * 0.12), (hx + sd * s * 0.2, hy + s * 0.1)], skin, smooth=False, shadow=0.1)
        f.shape([(hx + sd * s * 0.07, hy - s * 0.15), (hx + sd * s * 0.12, hy - s * 0.38), (hx + sd * s * 0.15, hy - s * 0.14)], P.hx("f2e2c0"),
                smooth=False, shadow=0.0)
    f.shape(ellipse(hx, hy, s * 0.2, s * 0.18, 0, 20), skin, smooth=False, shadow=0.2)
    for sd in (-1, 1):
        flat_fill(f.cv, f.cv.ellipse_mask(hx + sd * s * 0.08 + s * 0.02, hy - s * 0.02, s * 0.045, s * 0.05), P.hx("f6e04a"), 0.95)
        flat_fill(f.cv, f.cv.ellipse_mask(hx + sd * s * 0.08 + s * 0.02, hy - s * 0.02, s * 0.015, s * 0.04), P.INK, 0.95)
    f.lines([(hx - s * 0.08, hy + s * 0.08, hx + s * 0.1, hy + s * 0.07)], s * 0.025, P.hx("3a1a1a"), 0.9)
    # ink pot in its arms
    f.ell(x, y - s * 0.12, s * 0.1, s * 0.09, P.hx("2a2438"), hi=0.7, gloss=0.6, shadow=0.1)


# ----------------------------------------------------------------------------------------
# SHAMAN (horned beast-folk)
# ----------------------------------------------------------------------------------------
def draw_shaman(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    furc = pal["fur"]
    # spear (behind the right hand)
    sx = f.shR[0] + hu * 0.55
    spear(f, sx, f.top + hu * 0.05, f.feet - hu * 0.05, col=pal["wood"], tassel=pal["cloth"], feathers=[P.WHITE_WARM, P.TERRACOTTA])
    # mane down the back and a tail
    hx, hy = f.head_c
    r = rng(cv.seed, "mane")
    locks = []
    for k in range(13):
        t = k / 12
        root = (hx + (t - 0.5) * f.hw * 1.4, hy + f.hh * 0.3)
        tip = (hx + (t - 0.5) * f.hw * 3.4, f.sh_y + hu * (0.2 + 0.75 * math.sin(t * math.pi)) - r.random() * hu * 0.3)
        locks.append(f.hair_lock(root, tip, f.hw * 0.62, ctrl=(hx + (t - 0.5) * f.hw * 3.0, hy + f.hh * 0.9)))
    f.paint_hair(locks, pal["mane"], shine=False, each=False, key="maneb")
    f.tube([(cx + f.w_hip * 0.6, f.hip_y + hu * 0.4), (cx + f.w_hip * 1.35, f.knee_y + hu * 0.3), (cx + f.w_hip * 1.5, f.ankle_y - hu * 0.2)],
           [hu * 0.16, hu * 0.12, hu * 0.08], furc)
    f.paint_hair([blob(cx + f.w_hip * 1.52, f.ankle_y - hu * 0.1, hu * 0.13, hu * 0.2, rng(cv.seed, "tuft"), 0.2)], pal["mane"], shine=False)
    for s in (-1, 1):
        f.leg(s, pal["pants"], w_scale=1.05)
    for s in (-1, 1):
        f.boot(s, furc, top_y=f.ankle_y - hu * 0.3, cuff=pal["wrap"], toe=1.2)
        knee = f.kneeL if s < 0 else f.kneeR
        f.band([(knee[0] - hu * 0.28, knee[1] + hu * 0.25), (knee[0] + hu * 0.28, knee[1] + hu * 0.3)], hu * 0.1, pal["wrap"], cap=False)
    f.neck(furc)
    t = f.torso(furc)
    f.lines([(cx - f.w_chest * 0.55, f.chest_y + hu * 0.35, cx - hu * 0.05, f.chest_y + hu * 0.42),
             (cx + hu * 0.05, f.chest_y + hu * 0.42, cx + f.w_chest * 0.55, f.chest_y + hu * 0.35)], hu * 0.03, P.shadow_of(furc, 1.0), 0.5)
    lt = cv.mask(catmull(A([(cx - f.w_chest * 0.5, f.chest_y), (cx + f.w_chest * 0.5, f.chest_y), (cx + f.w_waist * 0.6, f.waist_y + hu * 0.2),
                            (cx - f.w_waist * 0.6, f.waist_y + hu * 0.2)]), True, 5)) * t
    cv.atop(P.light_of(furc, 1.0), lt * 0.4)
    # loin panel + belt with charms
    lp = [(cx - f.w_waist * 0.55, f.waist_y + hu * 0.15), (cx + f.w_waist * 0.55, f.waist_y + hu * 0.15), (cx + f.w_waist * 0.5, f.knee_y + hu * 0.1),
          (cx, f.knee_y + hu * 0.25), (cx - f.w_waist * 0.5, f.knee_y + hu * 0.1)]
    lm = f.shape(lp, pal["cloth"], n=5)
    f.trim([(cx - f.w_waist * 0.5, f.knee_y + hu * 0.05), (cx, f.knee_y + hu * 0.22), (cx + f.w_waist * 0.5, f.knee_y + hu * 0.05)], hu * 0.09,
           pal["trim"], motif=pal["wood"])
    f.trim([(cx, f.waist_y + hu * 0.3), (cx, f.knee_y - hu * 0.1)], hu * 0.08, pal["trim"])
    f.belt(f.waist_y + hu * 0.12, hu * 0.22, pal["leather"], buckle=P.CREAM)
    for k, bx in enumerate((cx - f.w_hip * 0.8, cx + f.w_hip * 0.55, cx + f.w_hip * 0.9)):
        totem_charm(f, bx, f.hip_y + hu * 0.35 + (k % 2) * hu * 0.12, hu * 0.42, [pal["wood"], P.mix(pal["wood"], P.TEAL, 0.4), P.mix(pal["wood"], P.TERRACOTTA, 0.4)][k])
    # open vest/sash over one shoulder
    f.band([(cx - f.w_sh * 0.8, f.sh_y + hu * 0.1), (cx - f.w_chest * 0.1, f.chest_y + hu * 0.25), (cx + f.w_waist * 0.9, f.waist_y + hu * 0.1)],
           hu * 0.32, pal["cloth"])
    necklace(f, [P.CREAM, pal["trim"], P.CREAM], y_off=0.2, n=7, r=0.06, sag=0.45)
    # arms (fur), bracers
    el = (f.shL[0] - hu * 0.3, f.shL[1] + hu * 1.05)
    wr = (f.shL[0] - hu * 0.3, f.shL[1] + hu * 2.05)
    f.tube([f.shL, el, wr], [f.w_uarm, f.w_farm * 1.05, f.w_wrist * 1.2], furc)
    f.tube([(el[0] + (wr[0] - el[0]) * 0.35, el[1] + (wr[1] - el[1]) * 0.35), wr], [f.w_farm * 1.25, f.w_wrist * 1.45], pal["leather"], cap=False)
    f.hand(wr[0], wr[1] + hu * 0.05, 0.0, fist=True, col=furc, s=1.15)
    hand = (sx - hu * 0.02, f.waist_y + hu * 0.05)
    el = (f.shR[0] + hu * 0.35, f.shR[1] + hu * 1.0)
    f.tube([f.shR, el, hand], [f.w_uarm, f.w_farm * 1.05, f.w_wrist * 1.2], furc)
    f.tube([(el[0] + (hand[0] - el[0]) * 0.35, el[1] + (hand[1] - el[1]) * 0.35), hand], [f.w_farm * 1.25, f.w_wrist * 1.45], pal["leather"], cap=False)
    f.hand(hand[0], hand[1] - hu * 0.05, 0.0, fist=True, col=furc, s=1.15)
    # shoulder pad of fur
    fur(f, [(cx + f.w_sh * 0.4, f.sh_y + hu * 0.0), (cx + f.w_sh * 1.05, f.sh_y + hu * 0.1), (cx + f.w_sh * 1.15, f.sh_y + hu * 0.45)], hu * 0.3,
        pal["mane"], key="sp")
    beast_head(f, furc, pal["muzzle"], pal["horn"], pal["mane"], look.eyes)
    return f

def char_job(key, draw, look, pal, bg):
    cv = new_canvas(key)
    f = draw(cv, look, pal)
    box = portrait_box(f, scale=2.45, dy=0.4) if draw is draw_shaman else portrait_box(f)
    sprite, portrait = finish_char(cv, box, bg)
    out = {key: sprite}
    pkey = "portrait_" + key.split("_", 1)[1]
    out[pkey] = portrait
    return out



# ----------------------------------------------------------------------------------------
# headwear
# ----------------------------------------------------------------------------------------
def straw_hat(f, col=P.hx("e8c97a"), band=P.hx("b8452f")):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    brim = f.shape(ellipse(hx, hy - hh * 0.55, hw * 1.9, hh * 0.36, -0.04, 48), col, smooth=False, shadow=0.35, hi=0.4)
    crown = f.shape([(hx - hw * 0.95, hy - hh * 0.55), (hx - hw * 0.85, hy - hh * 1.15), (hx, hy - hh * 1.32), (hx + hw * 0.85, hy - hh * 1.15),
                     (hx + hw * 0.95, hy - hh * 0.55)], col, n=6, hi=0.4)
    segs = []
    for k in range(18):
        a = k / 18 * 2 * math.pi
        segs.append((hx + math.cos(a) * hw * 1.0, hy - hh * 0.55 + math.sin(a) * hh * 0.18, hx + math.cos(a) * hw * 1.85, hy - hh * 0.55 + math.sin(a) * hh * 0.34))
    f.lines(segs, hw * 0.02, P.shadow_of(col, 1.0), 0.5, clip=brim)
    f.band([(hx - hw * 0.95, hy - hh * 0.68), (hx, hy - hh * 0.6), (hx + hw * 0.95, hy - hh * 0.68)], hh * 0.14, band, cap=False)


def wide_hat(f, col, band=P.HONEY, feather=None):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    if feather:
        f.shape([(hx + hw * 0.6, hy - hh * 1.0), (hx + hw * 1.6, hy - hh * 1.9), (hx + hw * 1.9, hy - hh * 1.75), (hx + hw * 0.9, hy - hh * 0.9)], feather,
                n=5)
    f.shape(catmull(A([(hx - hw * 1.7, hy - hh * 0.45), (hx, hy - hh * 0.75), (hx + hw * 1.75, hy - hh * 0.5), (hx + hw * 1.5, hy - hh * 0.3),
                       (hx, hy - hh * 0.5), (hx - hw * 1.5, hy - hh * 0.28)]), True, 6), col, smooth=False, shadow=0.35)
    f.shape([(hx - hw * 0.9, hy - hh * 0.55), (hx - hw * 0.8, hy - hh * 1.2), (hx - hw * 0.1, hy - hh * 1.35), (hx + hw * 0.1, hy - hh * 1.15),
             (hx + hw * 0.8, hy - hh * 1.25), (hx + hw * 0.92, hy - hh * 0.55)], col, n=5)
    f.band([(hx - hw * 0.9, hy - hh * 0.7), (hx + hw * 0.92, hy - hh * 0.7)], hh * 0.13, band, cap=False)


def headscarf(f, col, knot=True, pattern=None):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    pts = [(hx - hw * 1.12, hy + hh * 0.15), (hx - hw * 1.15, hy - hh * 0.55), (hx - hw * 0.6, hy - hh * 1.12), (hx + hw * 0.1, hy - hh * 1.2),
           (hx + hw * 0.8, hy - hh * 1.05), (hx + hw * 1.15, hy - hh * 0.5), (hx + hw * 1.1, hy + hh * 0.15), (hx + hw * 0.8, hy - hh * 0.35),
           (hx, hy - hh * 0.5), (hx - hw * 0.8, hy - hh * 0.35)]
    m = f.shape(pts, col, n=5)
    f.folds(m, [(hx - hw * 0.6, hy - hh * 0.9, hx - hw * 0.9, hy - hh * 0.2), (hx + hw * 0.3, hy - hh * 1.0, hx + hw * 0.8, hy - hh * 0.3)], col, 0.4,
            hw * 0.04)
    if pattern:
        r = rng(f.cv.seed, "scarfdots")
        for k in range(9):
            a = r.random() * math.pi + math.pi
            d = r.random() * 0.8
            f.ell(hx + math.cos(a) * hw * d, hy - hh * 0.55 + math.sin(a) * hh * 0.5 * d, hw * 0.06, hw * 0.06, pattern, line=0, shadow=0)
    if knot:
        kx, ky = hx + hw * 1.05, hy - hh * 0.25
        f.ell(kx, ky, hw * 0.18, hw * 0.15, col)
        for s in (-1, 1):
            f.shape([(kx, ky), (kx + hw * 0.35, ky + s * hh * 0.1 + hh * 0.2), (kx + hw * 0.5, ky + hh * 0.55 + s * hh * 0.1), (kx + hw * 0.1, ky + hh * 0.2)],
                    col, n=4)


def kettle_helm(f, col, trim=GOLD):
    hx, hy = f.head_c
    hw, hh = f.hw, f.hh
    f.shape(ellipse(hx, hy - hh * 0.45, hw * 1.55, hh * 0.3, -0.03, 40), col, smooth=False, shadow=0.35, hi=0.6, gloss=0.4)
    f.shape([(hx - hw * 1.0, hy - hh * 0.45), (hx - hw * 0.95, hy - hh * 1.0), (hx, hy - hh * 1.3), (hx + hw * 0.95, hy - hh * 1.0),
             (hx + hw * 1.0, hy - hh * 0.45)], col, n=6, hi=0.65, gloss=0.5)
    f.band([(hx, hy - hh * 1.28), (hx, hy - hh * 0.5)], hw * 0.12, trim, cap=False)
    f.band([(hx - hw * 1.0, hy - hh * 0.55), (hx + hw * 1.0, hy - hh * 0.55)], hw * 0.1, trim, cap=False)


def hood_down(f, col):
    cx, hu = f.cx, f.hu
    f.shape([(cx - f.w_sh * 0.95, f.sh_y + hu * 0.1), (cx - f.w_neck * 1.3, f.chin - hu * 0.1), (cx + f.w_neck * 1.3, f.chin - hu * 0.1),
             (cx + f.w_sh * 0.95, f.sh_y + hu * 0.1), (cx + f.w_sh * 0.6, f.sh_y + hu * 0.45), (cx, f.sh_y + hu * 0.55),
             (cx - f.w_sh * 0.6, f.sh_y + hu * 0.45)], col, n=5)


def apron(f, col, top=None, hem=None, bib=True):
    cx, hu = f.cx, f.hu
    top = top or f.waist_y + hu * 0.05
    hem = hem or f.knee_y + hu * 0.2
    pts = [(cx - f.w_waist * 0.95, top), (cx + f.w_waist * 0.95, top), (cx + f.w_hip * 1.0, hem), (cx, hem + hu * 0.05), (cx - f.w_hip * 1.0, hem)]
    m = f.shape(pts, col, n=5)
    f.folds(m, [(cx - hu * 0.2, top + hu * 0.3, cx - hu * 0.3, hem), (cx + hu * 0.25, top + hu * 0.3, cx + hu * 0.35, hem)], col, 0.35, hu * 0.03)
    f.shape([(cx - hu * 0.3, top + hu * 0.45), (cx + hu * 0.3, top + hu * 0.45), (cx + hu * 0.28, top + hu * 0.8), (cx - hu * 0.28, top + hu * 0.8)],
            P.scale_v(col, 0.93), smooth=False, shadow=0.15)
    if bib:
        f.shape([(cx - f.w_chest * 0.55, f.chest_y - hu * 0.1), (cx + f.w_chest * 0.55, f.chest_y - hu * 0.1), (cx + f.w_waist * 0.8, top + hu * 0.05),
                 (cx - f.w_waist * 0.8, top + hu * 0.05)], col, smooth=False)
        for s in (-1, 1):
            f.band([(cx + s * f.w_chest * 0.5, f.chest_y - hu * 0.08), (cx + s * f.w_neck * 0.9, f.neck_y + hu * 0.1)], hu * 0.06, col)
    f.band([(cx - f.w_waist * 1.05, top), (cx + f.w_waist * 1.05, top)], hu * 0.08, P.scale_v(col, 0.88), cap=False)
    return m


def basket(f, x, y, s, flowers=True):
    hu = f.hu
    f.band([(x - s * 0.45, y - s * 0.2), (x, y - s * 0.95), (x + s * 0.45, y - s * 0.2)], s * 0.08, P.THATCH_DK)
    if flowers:
        r = rng(f.cv.seed, "basketfl")
        for k in range(7):
            fx = x + (r.random() - 0.5) * s * 0.8
            fy = y - s * 0.25 - r.random() * s * 0.2
            f.ell(fx, fy, s * 0.11, s * 0.1, [P.ROSE, P.hx("f6cf4a"), P.WHITE_WARM, P.LAVENDER][k % 4], hi=0.5, shadow=0.15)
    m = f.shape([(x - s * 0.55, y - s * 0.25), (x + s * 0.55, y - s * 0.25), (x + s * 0.45, y + s * 0.25), (x - s * 0.45, y + s * 0.25)], P.THATCH,
                smooth=True, n=4)
    f.lines([(x - s * 0.5, y - s * 0.05, x + s * 0.5, y - s * 0.05), (x - s * 0.47, y + s * 0.1, x + s * 0.47, y + s * 0.1)], s * 0.03,
            P.THATCH_DK, 0.6, clip=m)


def robe(f, col, hem=None, w_hem=1.5, sash=None, trim=None, sleeves=True):
    cx, hu = f.cx, f.hu
    hem = hem or f.feet - hu * 0.05
    skirt(f, f.waist_y, hem, f.w_hip * 0.95, f.w_hip * w_hem, col, folds=5, trim=trim)
    f.torso(col, bottom=f.hip_y + hu * 0.2)
    f.shape([(cx - f.w_neck * 0.9, f.neck_y), (cx - f.w_neck * 0.1, f.neck_y + hu * 0.05), (cx + f.w_chest * 0.45, f.waist_y), (cx + f.w_chest * 0.25, f.waist_y)],
            trim or P.scale_v(col, 0.85), smooth=False, shadow=0.15)
    if sash:
        f.belt(f.waist_y + hu * 0.05, hu * 0.2, sash, buckle=None)
        f.shape([(cx - f.w_waist * 0.3, f.waist_y + hu * 0.1), (cx - f.w_waist * 0.05, f.waist_y + hu * 0.1), (cx - f.w_waist * 0.1, f.hip_y + hu * 0.7),
                 (cx - f.w_waist * 0.35, f.hip_y + hu * 0.65)], sash, n=4)


def wide_sleeve_arm(f, side, el, wr, col, cuff=None, hand_fist=True, hand_ang=0.0):
    f.arm(side, el, wr, sleeve=col, sleeve_w=1.35)
    if cuff:
        dx, dy = wr[0] - el[0], wr[1] - el[1]
        f.tube([(el[0] + dx * 0.72, el[1] + dy * 0.72), (el[0] + dx * 0.84, el[1] + dy * 0.84)], [f.w_farm * 1.6, f.w_farm * 1.6], cuff, cap=False)
    f.hand(wr[0], wr[1], hand_ang, fist=hand_fist)


# ----------------------------------------------------------------------------------------
# NPCs
# ----------------------------------------------------------------------------------------
BUILDS["plump"] = (0.8, 0.84, 0.8, 0.9, 0.34, 0.18, 0.21, 0.12, 0.25, 0.21, 0.13, 0.18)


def draw_elder(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    # cane (right hand)
    cx_ = f.shR[0] + hu * 0.45
    f.band([(cx_, f.feet - hu * 0.02), (cx_ + hu * 0.05, f.waist_y), (cx_ - hu * 0.02, f.chest_y + hu * 0.1)], hu * 0.1, P.WOOD)
    f.shape(catmull(A([(cx_ - hu * 0.02, f.chest_y + hu * 0.1), (cx_ + hu * 0.1, f.chest_y - hu * 0.25), (cx_ + hu * 0.35, f.chest_y - hu * 0.2),
                       (cx_ + hu * 0.3, f.chest_y), (cx_ + hu * 0.22, f.chest_y - hu * 0.1), (cx_ + hu * 0.1, f.chest_y - hu * 0.05), (cx_ + hu * 0.05, f.chest_y + hu * 0.12)]),
                    True, 4), P.WOOD, smooth=False)
    from props import hanging_lantern
    hanging_lantern(cv, cx_ + hu * 0.3, f.chest_y - hu * 0.05, hu * 0.16, paper=True)
    for s in (-1, 1):
        f.boot(s, P.hx("5a4636"), top_y=f.ankle_y - hu * 0.2)
    robe(f, pal["robe"], w_hem=1.45, sash=pal["sash"], trim=pal["trim"])
    # mantle over the shoulders
    f.shape([(cx - f.w_sh * 1.1, f.chest_y + hu * 0.45), (cx - f.w_sh * 0.95, f.sh_y), (cx - f.w_neck, f.neck_y), (cx + f.w_neck, f.neck_y),
             (cx + f.w_sh * 0.95, f.sh_y), (cx + f.w_sh * 1.1, f.chest_y + hu * 0.45), (cx, f.chest_y + hu * 0.7)], pal["mantle"], n=5)
    f.trim([(cx - f.w_sh * 1.1, f.chest_y + hu * 0.45), (cx, f.chest_y + hu * 0.7), (cx + f.w_sh * 1.1, f.chest_y + hu * 0.45)], hu * 0.07,
           pal["trim"], motif=pal["sash"])
    f.neck()
    el = (f.shL[0] - hu * 0.15, f.shL[1] + hu * 1.05)
    wide_sleeve_arm(f, -1, el, (cx - f.w_waist * 0.4, f.waist_y + hu * 0.15), pal["robe"], cuff=pal["trim"], hand_ang=0.8)
    wide_sleeve_arm(f, 1, (f.shR[0] + hu * 0.25, f.shR[1] + hu * 0.95), (cx_ - hu * 0.02, f.chest_y + hu * 0.35), pal["robe"], cuff=pal["trim"],
                    hand_ang=-0.3)
    human_head(f)
    return f


def draw_innkeeper(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    for s in (-1, 1):
        f.boot(s, P.hx("6b4a33"), top_y=f.ankle_y - hu * 0.15)
    skirt(f, f.waist_y, f.ankle_y - hu * 0.05, f.w_hip * 1.0, f.w_hip * 1.5, pal["dress"], folds=5)
    f.neck()
    f.torso(pal["dress"], neckline=0.4)
    f.shape([(cx - f.w_neck * 1.2, f.neck_y + hu * 0.05), (cx + f.w_neck * 1.2, f.neck_y + hu * 0.05), (cx + f.w_neck * 0.9, f.sh_y + hu * 0.35),
             (cx, f.sh_y + hu * 0.45), (cx - f.w_neck * 0.9, f.sh_y + hu * 0.35)], P.CREAM, n=5)
    apron(f, pal["apron"])
    # towel over the shoulder
    f.shape([(cx + f.w_sh * 0.4, f.sh_y), (cx + f.w_sh * 0.95, f.sh_y + hu * 0.05), (cx + f.w_sh * 1.05, f.chest_y + hu * 0.5),
             (cx + f.w_sh * 0.75, f.chest_y + hu * 0.45)], pal["towel"], n=4)
    # arms: right hand on hip, left holding a frothy mug
    f.arm(1, (f.shR[0] + hu * 0.45, f.shR[1] + hu * 0.8), (cx + f.w_waist * 1.0, f.waist_y + hu * 0.1), sleeve=pal["dress"], bare_from=0.6)
    f.hand(cx + f.w_waist * 1.0, f.waist_y + hu * 0.12, 2.6, fist=True)
    wr = (cx - f.w_chest * 0.65, f.chest_y + hu * 0.5)
    f.arm(-1, (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.0), wr, sleeve=pal["dress"], bare_from=0.6)
    mx, my = wr[0] - hu * 0.05, wr[1] - hu * 0.15
    f.shape([(mx - hu * 0.22, my - hu * 0.25), (mx + hu * 0.22, my - hu * 0.25), (mx + hu * 0.2, my + hu * 0.25), (mx - hu * 0.2, my + hu * 0.25)],
            P.WOOD_LIGHT, smooth=False, hi=0.4)
    f.band([(mx + hu * 0.2, my - hu * 0.15), (mx + hu * 0.38, my - hu * 0.05), (mx + hu * 0.35, my + hu * 0.12), (mx + hu * 0.2, my + hu * 0.12)],
           hu * 0.05, P.WOOD, cap=False)
    f.shape([(mx - hu * 0.25, my - hu * 0.22), (mx - hu * 0.15, my - hu * 0.38), (mx + hu * 0.05, my - hu * 0.36), (mx + hu * 0.2, my - hu * 0.4),
             (mx + hu * 0.26, my - hu * 0.22)], P.WHITE_WARM, n=4)
    f.hand(wr[0] + hu * 0.05, wr[1], 0.5, fist=True)
    human_head(f, front_extra=lambda: headscarf(f, pal["scarf"], pattern=P.WHITE_WARM))
    return f


def draw_merchant(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    # backpack behind with rolled blanket and pot
    pk = [(cx - f.w_sh * 1.15, f.sh_y + hu * 0.1), (cx + f.w_sh * 1.15, f.sh_y + hu * 0.1), (cx + f.w_sh * 1.25, f.hip_y + hu * 0.2),
          (cx - f.w_sh * 1.25, f.hip_y + hu * 0.2)]
    f.shape(pk, pal["pack"], n=4)
    f.band([(cx - f.w_sh * 1.2, f.sh_y - hu * 0.25), (cx + f.w_sh * 1.2, f.sh_y - hu * 0.25)], hu * 0.36, pal["roll"])
    f.ell(cx + f.w_sh * 1.15, f.waist_y - hu * 0.2, hu * 0.25, hu * 0.25, P.mix(P.STONE_DK, P.TERRACOTTA, 0.4), hi=0.6, gloss=0.4)
    for s in (-1, 1):
        f.leg(s, pal["pants"])
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.knee_y + hu * 0.25, cuff=LEATHER)
    f.neck()
    f.torso(pal["shirt"])
    vest(f, pal["vest"], open_w=0.3, hem_y=f.hip_y + hu * 0.1, trim=pal["trim"])
    f.belt(f.waist_y + hu * 0.15, hu * 0.16, LEATHER, buckle=P.HONEY)
    px, py = cx + f.w_hip * 0.6, f.waist_y + hu * 0.4
    f.shape(catmull(A([(px - hu * 0.15, py - hu * 0.15), (px + hu * 0.15, py - hu * 0.15), (px + hu * 0.22, py + hu * 0.15), (px, py + hu * 0.25),
                       (px - hu * 0.22, py + hu * 0.15)]), True, 4), P.mix(LEATHER, P.TERRACOTTA, 0.3), smooth=False)
    for s in (-1, 1):
        f.band([(cx + s * f.w_sh * 0.6, f.sh_y + hu * 0.05), (cx + s * f.w_chest * 0.75, f.waist_y)], hu * 0.09, LEATHER)
    f.arm(-1, (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.05), (f.shL[0] - hu * 0.15, f.shL[1] + hu * 2.05), sleeve=pal["shirt"], bare_from=0.7)
    f.hand(f.shL[0] - hu * 0.15, f.shL[1] + hu * 2.08, 0.0, fist=True)
    wr = (f.shR[0] + hu * 0.1, f.chest_y + hu * 0.55)
    f.arm(1, (f.shR[0] + hu * 0.45, f.shR[1] + hu * 0.95), wr, sleeve=pal["shirt"], bare_from=0.7)
    for k in range(3):
        f.ell(wr[0] - hu * 0.1 + k * hu * 0.08, wr[1] - hu * 0.15 - k * hu * 0.05, hu * 0.09, hu * 0.05, P.HONEY, hi=0.8, gloss=0.6)
    f.hand(wr[0], wr[1], -2.4, fist=False)
    human_head(f, front_extra=lambda: wide_hat(f, pal["hat"], band=pal["trim"], feather=P.hx("e86f8a")))
    return f


def draw_smith(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    for s in (-1, 1):
        f.leg(s, pal["pants"])
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.knee_y + hu * 0.3)
    f.neck()
    f.torso(pal["shirt"])
    apron(f, pal["apron"], top=f.waist_y - hu * 0.05, hem=f.knee_y + hu * 0.35)
    for s in (-1, 1):
        f.band([(cx + s * f.w_chest * 0.5, f.chest_y - hu * 0.1), (cx + s * f.w_sh * 0.7, f.sh_y + hu * 0.05)], hu * 0.08, pal["apron"])
    # rolled sleeves, thick gloves; hammer in the right hand, tongs in the left
    f.arm(-1, (f.shL[0] - hu * 0.3, f.shL[1] + hu * 1.0), (f.shL[0] - hu * 0.3, f.shL[1] + hu * 1.95), sleeve=pal["shirt"], bare_from=0.55,
          glove=pal["glove"])
    f.band([(f.shL[0] - hu * 0.35, f.shL[1] + hu * 1.95), (f.shL[0] - hu * 0.5, f.knee_y)], hu * 0.05, STEEL_DK)
    f.hand(f.shL[0] - hu * 0.3, f.shL[1] + hu * 2.0, 0.0, fist=True, col=pal["glove"])
    hand = (f.shR[0] + hu * 0.35, f.hip_y + hu * 0.0)
    f.arm(1, (f.shR[0] + hu * 0.35, f.shR[1] + hu * 1.0), hand, sleeve=pal["shirt"], bare_from=0.55, glove=pal["glove"])
    hammer(f, (hand[0], hand[1] - hu * 0.15), (hand[0] + hu * 0.05, hand[1] + hu * 0.9), col=STEEL_DK, w=0.7)
    f.hand(hand[0], hand[1], 0.0, fist=True, col=pal["glove"])
    human_head(f)
    return f


def draw_villager_a(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    for s in (-1, 1):
        f.boot(s, P.hx("6b4a33"), top_y=f.ankle_y - hu * 0.15)
    skirt(f, f.waist_y, f.ankle_y - hu * 0.1, f.w_hip * 0.95, f.w_hip * 1.45, pal["dress"], folds=5, trim=pal["trim"])
    f.neck()
    f.torso(pal["dress"], neckline=0.35)
    f.shape([(cx - f.w_waist * 1.0, f.chest_y + hu * 0.2), (cx + f.w_waist * 1.0, f.chest_y + hu * 0.2), (cx + f.w_waist * 1.05, f.waist_y + hu * 0.1),
             (cx - f.w_waist * 1.05, f.waist_y + hu * 0.1)], pal["bodice"], smooth=False)
    f.lines([(cx - hu * 0.08, f.chest_y + hu * (0.3 + k * 0.12), cx + hu * 0.08, f.chest_y + hu * (0.36 + k * 0.12)) for k in range(4)], hu * 0.02,
            P.CREAM, 0.8)
    apron(f, pal["apron"], bib=False)
    el = (f.shR[0] + hu * 0.35, f.shR[1] + hu * 1.0)
    wr = (cx + f.w_waist * 0.7, f.waist_y + hu * 0.05)
    f.arm(1, el, wr, sleeve=pal["blouse"], bare_from=0.6)
    basket(f, el[0] + hu * 0.25, el[1] + hu * 0.3, hu * 0.85)
    f.hand(wr[0], wr[1], 1.6, fist=True)
    f.arm(-1, (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.05), (f.shL[0] - hu * 0.12, f.shL[1] + hu * 2.0), sleeve=pal["blouse"], bare_from=0.6)
    f.hand(f.shL[0] - hu * 0.12, f.shL[1] + hu * 2.02, 0.0, fist=False)
    human_head(f, front_extra=lambda: headscarf(f, pal["scarf"], pattern=P.WHITE_WARM))
    return f


def draw_villager_b(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    hx_ = f.shL[0] - hu * 0.45
    f.band([(hx_, f.feet - hu * 0.05), (hx_, f.top - hu * 0.2)], hu * 0.08, P.WOOD)
    f.shape([(hx_ - hu * 0.05, f.top - hu * 0.15), (hx_ - hu * 0.45, f.top - hu * 0.1), (hx_ - hu * 0.5, f.top + hu * 0.2), (hx_ - hu * 0.05, f.top + hu * 0.05)],
            STEEL, smooth=False, hi=0.6)
    for s in (-1, 1):
        f.leg(s, pal["pants"])
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.ankle_y - hu * 0.35)
    f.neck()
    f.torso(pal["shirt"])
    # overall bib with straps
    f.shape([(cx - f.w_chest * 0.6, f.chest_y), (cx + f.w_chest * 0.6, f.chest_y), (cx + f.w_waist * 0.95, f.waist_y + hu * 0.1),
             (cx + f.w_hip, f.hip_y + hu * 0.3), (cx - f.w_hip, f.hip_y + hu * 0.3), (cx - f.w_waist * 0.95, f.waist_y + hu * 0.1)], pal["pants"], n=4)
    for s in (-1, 1):
        f.band([(cx + s * f.w_chest * 0.5, f.chest_y + hu * 0.02), (cx + s * f.w_sh * 0.6, f.sh_y + hu * 0.05)], hu * 0.08, pal["pants"])
        f.ell(cx + s * f.w_chest * 0.5, f.chest_y + hu * 0.05, hu * 0.05, hu * 0.05, P.HONEY, hi=0.6)
    f.shape([(cx - hu * 0.25, f.chest_y + hu * 0.2), (cx + hu * 0.25, f.chest_y + hu * 0.2), (cx + hu * 0.22, f.chest_y + hu * 0.5), (cx - hu * 0.22, f.chest_y + hu * 0.5)],
            P.scale_v(pal["pants"], 0.9), smooth=False, shadow=0.15)
    f.arm(-1, (f.shL[0] - hu * 0.3, f.shL[1] + hu * 0.95), (hx_ + hu * 0.05, f.chest_y + hu * 0.55), sleeve=pal["shirt"], bare_from=0.65)
    f.hand(hx_ + hu * 0.02, f.chest_y + hu * 0.55, 1.5, fist=True)
    f.arm(1, (f.shR[0] + hu * 0.2, f.shR[1] + hu * 1.05), (f.shR[0] + hu * 0.25, f.shR[1] + hu * 2.0), sleeve=pal["shirt"], bare_from=0.65)
    f.hand(f.shR[0] + hu * 0.25, f.shR[1] + hu * 2.02, 0.0, fist=True)
    human_head(f, front_extra=lambda: straw_hat(f))
    return f


def draw_child(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    for s in (-1, 1):
        f.leg(s, None, bare=True)
    for s in (-1, 1):
        f.boot(s, pal["boots"], top_y=f.ankle_y - hu * 0.35, cuff=P.CREAM)
    shorts(f, pal["shorts"], hem_dy=0.55, baggy=1.1)
    f.neck()
    f.torso(pal["tunic"], bottom=f.hip_y + hu * 0.25)
    f.belt(f.waist_y + hu * 0.15, hu * 0.14, LEATHER, buckle=P.HONEY)
    f.shape([(cx - f.w_neck * 1.3, f.neck_y), (cx + f.w_neck * 1.3, f.neck_y), (cx + f.w_neck * 1.5, f.sh_y + hu * 0.2), (cx, f.sh_y + hu * 0.35),
             (cx - f.w_neck * 1.5, f.sh_y + hu * 0.2)], pal["collar"], n=5)
    # holding a little paper lantern on a stick
    wr = (f.shR[0] + hu * 0.35, f.chest_y + hu * 0.45)
    f.arm(1, (f.shR[0] + hu * 0.4, f.shR[1] + hu * 0.8), wr, sleeve=pal["tunic"], bare_from=0.6)
    f.band([(wr[0], wr[1]), (wr[0] + hu * 0.25, wr[1] - hu * 1.0)], hu * 0.05, P.WOOD)
    from props import hanging_lantern
    hanging_lantern(cv, wr[0] + hu * 0.25 + hu * 0.0, wr[1] - hu * 1.0, hu * 0.22, paper=True)
    f.hand(wr[0], wr[1], -2.8, fist=True)
    f.arm(-1, (f.shL[0] - hu * 0.2, f.shL[1] + hu * 0.95), (f.shL[0] - hu * 0.15, f.shL[1] + hu * 1.85), sleeve=pal["tunic"], bare_from=0.6)
    f.hand(f.shL[0] - hu * 0.15, f.shL[1] + hu * 1.87, 0.0, fist=False)
    human_head(f)
    return f


def draw_guard(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    spear(f, f.shR[0] + hu * 0.5, f.top - hu * 0.5, f.feet - hu * 0.05, col=P.WOOD, tassel=pal["tabard"])
    for s in (-1, 1):
        m = f.leg(s, pal["mail"])
        mail_texture(f, m, pal["mail"])
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.knee_y + hu * 0.1, cuff=STEEL)
    f.neck()
    m = f.torso(pal["mail"])
    mail_texture(f, m, pal["mail"])
    tb = [(cx - f.w_chest * 0.7, f.sh_y + hu * 0.3), (cx + f.w_chest * 0.7, f.sh_y + hu * 0.3), (cx + f.w_waist * 0.9, f.waist_y),
          (cx + f.w_hip * 0.85, f.knee_y - hu * 0.1), (cx - f.w_hip * 0.85, f.knee_y - hu * 0.1), (cx - f.w_waist * 0.9, f.waist_y)]
    tm = f.shape(tb, pal["tabard"], n=5)
    half = cv.mask(A([(cx, f.sh_y), (cx + hu * 3, f.sh_y), (cx + hu * 3, f.knee_y), (cx, f.knee_y)])) * tm
    cv.atop(pal["tabard2"], half * 0.95)
    from props import hanging_lantern
    f.belt(f.waist_y + hu * 0.1, hu * 0.18, LEATHER, buckle=P.HONEY)
    f.pauldron(-1, STEEL, layers=2, size=0.95)
    f.pauldron(1, STEEL, layers=2, size=0.95)
    # round shield on the left arm
    f.arm(-1, (f.shL[0] - hu * 0.25, f.shL[1] + hu * 1.0), (cx - f.w_waist * 0.6, f.waist_y + hu * 0.3), sleeve=pal["mail"], glove=LEATHER)
    sx, sy = cx - f.w_sh * 0.8, f.waist_y + hu * 0.35
    f.ell(sx, sy, hu * 0.62, hu * 0.62, STEEL, shadow=0.35, hi=0.6, gloss=0.4)
    f.ell(sx, sy, hu * 0.52, hu * 0.52, pal["tabard"], hi=0.4)
    half = cv.mask(A([(sx, sy - hu), (sx + hu, sy - hu), (sx + hu, sy + hu), (sx, sy + hu)])) * cv.ellipse_mask(sx, sy, hu * 0.52, hu * 0.52)
    cv.atop(pal["tabard2"], half * 0.95)
    f.ell(sx, sy, hu * 0.14, hu * 0.14, P.HONEY, hi=0.7, gloss=0.5)
    hand = (f.shR[0] + hu * 0.48, f.waist_y)
    f.arm(1, (f.shR[0] + hu * 0.35, f.shR[1] + hu * 1.0), hand, sleeve=pal["mail"], glove=LEATHER)
    f.hand(hand[0], hand[1], 0.0, fist=True, col=LEATHER)
    human_head(f, front_extra=lambda: kettle_helm(f, STEEL))
    return f


def draw_trainer(cv, look, pal):
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    for s in (-1, 1):
        f.boot(s, LEATHER_DK, top_y=f.ankle_y - hu * 0.2)
    robe(f, pal["robe"], w_hem=1.5, sash=pal["sash"], trim=pal["trim"])
    # stole
    for s in (-1, 1):
        f.shape([(cx + s * f.w_neck * 0.9, f.neck_y), (cx + s * f.w_neck * 2.0, f.sh_y + hu * 0.1), (cx + s * f.w_chest * 0.55, f.knee_y + hu * 0.2),
                 (cx + s * f.w_chest * 0.2, f.knee_y + hu * 0.2), (cx + s * f.w_neck * 0.6, f.sh_y + hu * 0.5)], pal["stole"], n=4)
        f.trim([(cx + s * f.w_chest * 0.55, f.knee_y + hu * 0.18), (cx + s * f.w_chest * 0.2, f.knee_y + hu * 0.18)], hu * 0.07, pal["trim"])
    hood_down(f, pal["robe"])
    f.neck()
    gx, gy = cx - f.w_chest * 0.15, f.chest_y + hu * 0.5
    wide_sleeve_arm(f, -1, (f.shL[0] - hu * 0.15, f.shL[1] + hu * 1.0), (gx - hu * 0.3, gy + hu * 0.1), pal["robe"], cuff=pal["trim"], hand_ang=-1.2)
    f.shape([(gx - hu * 0.45, gy - hu * 0.3), (gx + hu * 0.35, gy - hu * 0.32), (gx + hu * 0.38, gy + hu * 0.25), (gx - hu * 0.42, gy + hu * 0.27)],
            pal["book"], smooth=False, hi=0.4)
    f.shape([(gx - hu * 0.4, gy - hu * 0.36), (gx + hu * 0.3, gy - hu * 0.38), (gx + hu * 0.3, gy - hu * 0.28), (gx - hu * 0.4, gy - hu * 0.26)],
            P.CREAM, smooth=False, shadow=0, line=0.6)
    f.ell(gx, gy, hu * 0.08, hu * 0.08, pal["trim"], hi=0.6, gloss=0.5)
    wide_sleeve_arm(f, 1, (f.shR[0] + hu * 0.25, f.shR[1] + hu * 1.0), (gx + hu * 0.3, gy + hu * 0.05), pal["robe"], cuff=pal["trim"], hand_ang=1.2)
    human_head(f)
    return f


def draw_spirit(cv, look, pal):
    """Friendly forest spirit (kodama-like): small pale body, round head with three dark hollows, soft glow."""
    f = Fig(cv, look)
    hu, cx = f.hu, f.cx
    body = pal["body"]
    gl = pal["glow"]
    f.glow(cx, f.feet - hu * 1.2, hu * 2.4, gl, 0.25)
    for s in (-1, 1):
        f.tube([(cx + s * hu * 0.15, f.hip_y + hu * 0.1), (cx + s * hu * 0.18, f.feet - hu * 0.1)], [hu * 0.15, hu * 0.12], body, cel=0.4)
    tor = [(cx - hu * 0.26, f.sh_y + hu * 0.1), (cx + hu * 0.26, f.sh_y + hu * 0.1), (cx + hu * 0.32, f.hip_y + hu * 0.2), (cx - hu * 0.32, f.hip_y + hu * 0.2)]
    f.shape(tor, body, cel=0.4, hi=0.4)
    for s in (-1, 1):
        f.tube([(cx + s * hu * 0.28, f.sh_y + hu * 0.2), (cx + s * hu * 0.45, f.sh_y + hu * 0.6), (cx + s * hu * 0.5, f.waist_y + hu * 0.15)],
               [hu * 0.1, hu * 0.085, hu * 0.07], body, cel=0.4)
    hx, hy = f.head_c
    head = catmull(A([(hx - hu * 0.55, hy + hu * 0.1), (hx - hu * 0.5, hy - hu * 0.35), (hx - hu * 0.1, hy - hu * 0.6), (hx + hu * 0.35, hy - hu * 0.5),
                      (hx + hu * 0.58, hy - hu * 0.05), (hx + hu * 0.45, hy + hu * 0.4), (hx, hy + hu * 0.5), (hx - hu * 0.42, hy + hu * 0.42)]), True, 6)
    m = f.shape(head, body, smooth=False, cel=0.4, hi=0.5, shadow=0.2)
    t = look.turn
    for (ex, ey, rx, ry) in ((hx - hu * 0.18 + t * hu * 0.1, hy - hu * 0.02, hu * 0.08, hu * 0.1), (hx + hu * 0.18 + t * hu * 0.1, hy - hu * 0.04, hu * 0.07, hu * 0.09),
                             (hx + t * hu * 0.12, hy + hu * 0.22, hu * 0.06, hu * 0.08)):
        flat_fill(cv, cv.ellipse_mask(ex, ey, rx, ry), P.mix(P.INK, P.FOREST_DK, 0.3), 0.92)
    # a sprout on top
    f.band([(hx + hu * 0.05, hy - hu * 0.55), (hx + hu * 0.1, hy - hu * 0.8)], hu * 0.04, P.MOSS)
    for s in (-1, 1):
        from environment import leaf_poly
        f.shape(leaf_poly(hx + hu * 0.1, hy - hu * 0.78, hu * 0.25, -math.pi / 2 + s * 0.9, 0.5), P.mix(P.MOSS, P.GRASS_LIGHT, 0.5), smooth=False)
    cv.atop(gl, cv.a * 0.12)
    return f


NPC_BG = (P.hx("efe6d2"), P.hx("a8bc8a"))

HUNTER = dict(leather=LEATHER, boots=LEATHER_DK, wrap=P.hx("b98a5a"), shorts=P.hx("3f8f86"), trim=P.hx("f2c14e"), vest=P.hx("a8774c"),
              fur=P.hx("eadfc8"), sash=P.hx("e8b04f"), bow=P.WOOD)
PALADIN = dict(cape=P.hx("3f6fae"), mail=P.hx("8c93a3"), plate=P.hx("c9d0dc"), trim=GOLD, tabard=P.hx("f6efe0"), accent=P.hx("3f6fae"),
               belt=LEATHER, shield=P.hx("3f6fae"))
MAGE = dict(staff=P.hx("4a3b4f"), orb=P.hx("b27cf0"), dress=P.hx("4a3560"), strip=P.hx("221a2c"), metal=P.hx("d6d9e2"), fur=P.hx("ece6ea"),
            belt=P.hx("5a3a3a"))
PRIEST = dict(gold=GOLD, obi=P.hx("f2c14e"), boots=P.hx("3a3442"), skirt=P.hx("3f6fb0"), trim=P.hx("e86f8a"), top=P.hx("f7f3ec"),
              flower1=P.hx("f08aa0"), flower2=P.hx("8fb8de"), cord=P.hx("d9483b"))
ROGUE = dict(boots=P.hx("6b4a33"), accent=P.hx("f2c14e"), shorts=P.hx("5f8f45"), top=P.hx("f2b33d"), belt=LEATHER, metal=P.HONEY,
             scarf=P.hx("e8743b"), glove=P.hx("4a3b3a"))
WARLOCK = dict(coat=P.hx("2f3a6b"), lining=P.hx("1c2140"), trim=GOLD, pants=P.hx("2a2a3a"), boots=P.hx("1f1d2a"), vest=P.hx("e8e2f0"),
               sash=P.hx("7a4fb0"), rune=P.hx("b98cff"), glove=P.hx("2a2438"), book=P.hx("5a2f4f"))
SHAMAN = dict(fur=P.hx("7f8fb0"), mane=P.hx("d6cfe2"), muzzle=P.hx("c9cfe0"), horn=P.hx("efe6d2"), pants=P.hx("5a4636"), wrap=P.hx("c9783a"),
              cloth=P.hx("b8452f"), trim=P.hx("e8b04f"), wood=P.hx("9a6b45"), leather=LEATHER)


def variant(base, **kw):
    d = dict(base)
    d.update(kw)
    return d


CLASS_BG = {
    "warrior": (P.hx("f6d8c8"), P.hx("c0392b")), "hunter": (P.hx("e6edcf"), P.hx("6f9a3c")),
    "paladin": (P.hx("fbefcf"), P.hx("e6b84a")), "mage": (P.hx("e6dcf2"), P.hx("6d5aa8")),
    "priest": (P.hx("e3eef8"), P.hx("8fb8de")), "rogue": (P.hx("fae3cc"), P.hx("e88a3a")),
    "warlock": (P.hx("e4dbf2"), P.hx("7a4fb0")), "shaman": (P.hx("dde7f4"), P.hx("3f78b5")),
}

ROSTER = {
    # heroes
    "char_warrior": (draw_warrior, dict(hair=P.hx("3b2b2e"), eyes=P.hx("b5762e"), expr="stern", hair_style="spiky"), WARRIOR, CLASS_BG["warrior"]),
    "char_hunter": (draw_hunter, dict(hair=P.hx("3b2a22"), eyes=P.hx("4f8f5a"), skin=P.SKIN_TAN, hair_style="braids", band=P.hx("3f8f86"),
                                      expr="smile", height=1.84), HUNTER, CLASS_BG["hunter"]),
    "char_paladin": (draw_paladin, dict(hair=P.hx("e2c06a"), eyes=P.hx("4f7fc4"), skin=P.SKIN_FAIR, hair_style="neat", height=1.84), PALADIN,
                     CLASS_BG["paladin"]),
    "char_mage": (draw_mage, dict(build="f", height=1.74, hair=P.hx("2b2333"), eyes=P.hx("9a3a5a"), skin=P.SKIN_FAIR, hair_style="bun",
                                  accent=P.hx("b27cf0")), MAGE, CLASS_BG["mage"]),
    "char_priest": (draw_priest, dict(build="f", height=1.66, hair=P.hx("6b4a33"), eyes=P.hx("4f9a9a"), skin=P.SKIN_FAIR, hair_style="bob",
                                      accent=P.hx("e86f8a"), expr="smile"), PRIEST, CLASS_BG["priest"]),
    "char_rogue": (draw_rogue, dict(build="f", height=1.62, hair=P.hx("f0cf6a"), eyes=P.hx("5aa05a"), skin=P.SKIN_TAN, hair_style="rogue",
                                    expr="grin", turn=0.24), ROGUE, CLASS_BG["rogue"]),
    "char_warlock": (draw_warlock, dict(height=1.85, hair=P.hx("a8c0e0"), eyes=P.hx("8a5ad0"), skin=P.SKIN_FAIR, hair_style="swept_long"),
                     WARLOCK, CLASS_BG["warlock"]),
    "char_shaman": (draw_shaman, dict(build="big", height=1.95, heads=6.8, eyes=P.hx("e8b04f"), turn=0.14), SHAMAN, CLASS_BG["shaman"]),
    # companions (variations with their own palette / hair)
    "comp_kael": (draw_warrior, dict(height=1.82, hair=P.hx("5e5660"), streak=P.hx("b9b4bc"), eyes=P.hx("8a6a3a"), expr="stern",
                                     hair_style="swept_back", scar=True, skin=P.SKIN_TAN, turn=0.14),
                  variant(WARRIOR, coat=P.hx("8e2a28"), lining=P.hx("3a1414"), trim=P.hx("b89a5a"), plate=P.hx("4a4e58"),
                          under=P.hx("2e2c34"), sash=P.hx("4a3a36"), gem=P.hx("b89a5a"), blade=P.hx("8a8e98"), blade_w=0.52, notched=True,
                          tucked=True, flask=P.hx("b98a4a"), mail_skirt=P.hx("6f7480"), flare=1.6, collar_h=0.55), CLASS_BG["warrior"]),
    "comp_lys": (draw_mage, dict(build="f", height=1.74, hair=P.hx("24202c"), eyes=P.hx("7a5a9a"), skin=P.SKIN_FAIR, hair_style="braid_needles",
                                 accent=P.hx("8fb8e8"), expr="calm"),
                 variant(MAGE, dress=P.hx("2c3448"), strip=P.hx("161b26"), orb=P.hx("8fb0e0"), fur=P.hx("eae6ee"), belt=P.hx("3a3040"),
                         metal=P.hx("c9ccd6"), staff=P.hx("2c2a36")), CLASS_BG["mage"]),
    "comp_seren": (draw_priest, dict(build="f", height=1.66, hair=P.hx("2a2230"), streak=P.hx("f4f2f6"), eyes=P.hx("5a7ab0"), skin=P.SKIN_FAIR,
                                     hair_style="ponytail", accent=P.hx("8fb8de"), expr="smile"),
                   variant(PRIEST, top=P.hx("cfe2f2"), skirt=P.hx("4a3d78"), obi=P.hx("c4b0ea"), trim=P.hx("f6efe0"), flower1=P.WHITE_WARM,
                           flower2=P.hx("f08aa0"), cord=P.HONEY), CLASS_BG["priest"]),
    "comp_rook": (draw_hunter, dict(build="big", height=1.88, heads=6.8, hair=P.hx("c86a2e"), eyes=P.hx("4f8f5a"), skin=P.SKIN_BROWN,
                                    hair_style="bandana_braids", expr="grin", band=P.hx("3f6fb0")),
                  variant(HUNTER, shorts=P.hx("e8a03a"), vest=P.hx("3f6fb0"), trim=P.hx("f2e6c8"), fur=P.hx("b8a890"), sash=P.hx("d9483b"),
                          bow=P.hx("b89a74"), number=P.hx("f6efe0"), ringball=P.hx("e8743b")), CLASS_BG["hunter"]),
    "comp_pip": (draw_rogue, dict(build="f", height=1.52, heads=6.0, hair=P.hx("f2c86a"), eyes=P.hx("5aa05a"), skin=P.SKIN_LIGHT,
                                  hair_style="twin_buns", accent=P.hx("3fb0a8"), expr="grin", turn=0.22, goggles=True),
                 variant(ROGUE, scarf=P.hx("e8607a"), top=P.hx("3fb0a8"), shorts=P.hx("6a4f9a"), accent=P.hx("f2c14e"),
                         sleeve_l=P.hx("f2c14e"), sleeve_r=None), CLASS_BG["rogue"]),
    "comp_torvan": (draw_shaman, dict(build="big", height=1.98, heads=6.8, eyes=P.hx("d9a84a"), turn=0.12, horn_charms=True,
                                      markings=P.hx("4f6fa8")),
                    variant(SHAMAN, fur=P.hx("b8bcc4"), mane=P.hx("e6e6ee"), muzzle=P.hx("dcdee4"), horn=P.hx("b8a888"),
                            cloth=P.hx("4f7f4a"), trim=P.hx("c9a85a")), CLASS_BG["shaman"]),
    "comp_aldric": (draw_paladin, dict(height=1.78, hair=P.hx("f2cf6a"), eyes=P.hx("4f9ad0"), skin=P.SKIN_LIGHT, hair_style="spiky",
                                       expr="grin", turn=0.2),
                    variant(PALADIN, tabard=P.hx("f2c14e"), accent=P.hx("8fc4ec"), cape=P.hx("7fb6e2"), shield=P.hx("f6efe0"),
                            plate=P.hx("dfe3ea"), mail=P.hx("b4bcc8"), trim=P.hx("e8a03a"), bare_right=True, asym=0.35, faulds=1,
                            pauldron_layers=2), CLASS_BG["paladin"]),
    "comp_morwen": (draw_warlock, dict(build="f", height=1.74, hair=P.hx("1c1820"), eyes=P.hx("9a6ad8"), skin=P.SKIN_FAIR, hair_style="long"),
                    variant(WARLOCK, coat=P.hx("3a2440"), lining=P.hx("7a4fb0"), rune=P.hx("c48cff"), vest=P.hx("2a2030"),
                            sash=P.hx("8a3a5a"), trim=P.hx("c9ccd6"), imp=True), CLASS_BG["warlock"]),
    # villagers
    "npc_elder": (draw_elder, dict(build="old", height=1.64, heads=6.2, hair=P.hx("eeeae4"), beard=P.hx("f1ede6"), beard_len=1.6,
                                   eyes=P.hx("6a5a4a"), skin=P.SKIN_LIGHT, hair_style="bald_beard", expr="smile"),
                  dict(robe=P.hx("7a6a4f"), sash=P.hx("c9783a"), trim=P.hx("e8c97a"), mantle=P.hx("5f7a4f")), NPC_BG),
    "npc_innkeeper": (draw_innkeeper, dict(build="plump", height=1.66, hair=P.hx("8a4a2e"), eyes=P.hx("6a8a4a"), skin=P.SKIN_LIGHT,
                                           hair_style="short", expr="smile"),
                      dict(dress=P.hx("b8606a"), apron=P.hx("f6efe2"), towel=P.hx("8fb8de"), scarf=P.hx("4f8f6a")), NPC_BG),
    "npc_merchant": (draw_merchant, dict(height=1.76, hair=P.hx("5a3a26"), beard=P.hx("5a3a26"), beard_len=0.3, eyes=P.hx("4a6a8a"),
                                         skin=P.SKIN_LIGHT, hair_style="short", expr="smile"),
                     dict(pack=P.hx("8a6a45"), roll=P.hx("b8452f"), pants=P.hx("5a4a3a"), shirt=P.hx("efe2c8"), vest=P.hx("3f7a5a"),
                          trim=P.HONEY, hat=P.hx("6a4a3a")), NPC_BG),
    "npc_smith": (draw_smith, dict(build="big", height=1.78, heads=6.4, hair=P.hx("2a2220"), beard=P.hx("3a2a22"), beard_len=0.5,
                                   eyes=P.hx("6a4a3a"), skin=P.SKIN_TAN, hair_style="none", expr="stern"),
                  dict(pants=P.hx("4a4048"), shirt=P.hx("d8cbb0"), apron=P.hx("6b4a33"), glove=P.hx("8a6a45")), NPC_BG),
    "npc_villager_a": (draw_villager_a, dict(build="f", height=1.66, hair=P.hx("c08040"), eyes=P.hx("4f7f5a"), skin=P.SKIN_LIGHT,
                                             hair_style="short", expr="smile"),
                       dict(dress=P.hx("6f8fb6"), bodice=P.hx("4a6a8a"), apron=P.hx("f6efe2"), blouse=P.hx("f6efe2"), scarf=P.hx("e8b04f"),
                            trim=P.hx("f6efe2")), NPC_BG),
    "npc_villager_b": (draw_villager_b, dict(height=1.76, hair=P.hx("6a4a2a"), eyes=P.hx("4a5a6a"), skin=P.SKIN_TAN, hair_style="short"),
                       dict(pants=P.hx("6a7f9a"), shirt=P.hx("f0e4cc")), NPC_BG),
    "npc_child": (draw_child, dict(build="child", height=1.22, heads=4.9, hair=P.hx("8a5a32"), eyes=P.hx("5a8a4a"), skin=P.SKIN_LIGHT,
                                   hair_style="short", expr="smile"),
                  dict(boots=P.hx("6b4a33"), shorts=P.hx("5a6a8a"), tunic=P.hx("8cbf6a"), collar=P.hx("f6efe2")), NPC_BG),
    "npc_guard": (draw_guard, dict(height=1.82, hair=P.hx("4a3328"), eyes=P.hx("5a6a7a"), skin=P.SKIN_LIGHT, hair_style="short", expr="stern"),
                  dict(mail=P.hx("8c93a3"), tabard=P.hx("3f8f86"), tabard2=P.hx("f2e6c8")), NPC_BG),
    "npc_trainer": (draw_trainer, dict(height=1.8, hair=P.hx("8a8a92"), beard=P.hx("9a9aa2"), beard_len=0.4, eyes=P.hx("5a5a8a"),
                                       skin=P.SKIN_LIGHT, hair_style="neat"),
                    dict(robe=P.hx("5a6a9a"), sash=P.hx("e8b04f"), trim=P.hx("e8c97a"), stole=P.hx("8a3a5a"), book=P.hx("6a3a2e")), NPC_BG),
    "npc_spirit": (draw_spirit, dict(build="child", height=0.92, heads=2.6, turn=0.2, hair_style="none"),
                   dict(body=P.hx("eef4e6"), glow=P.hx("d8f4c0")), (P.hx("e6f2dc"), P.hx("7d9a55"))),
}


def register(reg):
    for key, (draw, lk, pal, bg) in ROSTER.items():
        reg.spec(key, "Character", size=(512, 1024), height=CANVAS_M, pivot=(0.5, 0.05), shadow=True)
        pkey = "portrait_" + key.split("_", 1)[1]
        reg.spec(pkey, "Portrait", size=(256, 256), height=1.0, pivot=(0.5, 0.5))
        reg.job([key, pkey], char_job, key, draw, Look(**lk), pal, bg)
