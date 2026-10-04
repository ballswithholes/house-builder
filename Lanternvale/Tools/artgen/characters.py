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
        glow(self.cv, x, y, r, col, s, clip=clip)

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

    def neck(self):
        hu = self.hu
        hx = self.head_c[0]
        pts = [(hx - self.w_neck / 2, self.chin - hu * 0.25), (hx + self.w_neck / 2, self.chin - hu * 0.25),
               (self.cx + self.w_neck * 0.62, self.sh_y + hu * 0.1), (self.cx - self.w_neck * 0.62, self.sh_y + hu * 0.1)]
        m = self.skin_part(pts, smooth=False, shadow=0)
        # shadow under the chin
        sh = self.cv.mask(ellipse(hx + hu * 0.03, self.chin - hu * 0.02, self.w_neck * 0.75, hu * 0.12))
        self.cv.atop(skin_lo(self.L.skin), sh * m * 0.75)
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
    # left arm hangs relaxed (coat sleeve, gauntlet)
    el = (f.shL[0] - hu * 0.2, f.shL[1] + hu * 1.12)
    wr = (f.shL[0] - hu * 0.14, f.shL[1] + hu * 2.1)
    f.arm(-1, el, wr, sleeve=coat, glove=plate, cuff=trim, sleeve_w=1.12)
    f.hand(wr[0], wr[1] + hu * 0.04, 0.1, fist=True, col=pal["glove"])
    # head
    hair_spiky(f, back=True)
    stand_collar(f, coat, lining, trim)
    f.ears()
    f.face()
    f.features()
    hair_spiky(f, back=False)
    # right arm: hand at the chest gripping the sword which rests on the shoulder; shoulder plate on top
    hand = (cx + f.w_sh * 0.42, f.chest_y + hu * 0.18)
    el = (f.shR[0] + hu * 0.22, f.waist_y - hu * 0.02)
    f.arm(1, el, hand, sleeve=coat, glove=plate, cuff=trim, sleeve_w=1.12)
    f.pauldron(1, plate, layers=3, size=1.05, trim=trim)
    ang = math.radians(29)
    d = (math.sin(ang), -math.cos(ang))
    guard = (hand[0] + d[0] * hu * 0.32, hand[1] + d[1] * hu * 0.32)
    tip = (guard[0] + d[0] * hu * 3.05, guard[1] + d[1] * hu * 3.05)
    blade(f, guard, tip, hu * 0.44, col=pal.get("blade", STEEL))
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


def char_job(key, draw, look, pal, bg):
    cv = new_canvas(key)
    f = draw(cv, look, pal)
    sprite, portrait = finish_char(cv, portrait_box(f), bg)
    out = {key: sprite}
    pkey = "portrait_" + key.split("_", 1)[1]
    out[pkey] = portrait
    return out


ROSTER = {
    "char_warrior": (draw_warrior, dict(hair=P.hx("3b2b2e"), eyes=P.hx("b5762e"), expr="stern", skin=P.SKIN_LIGHT), WARRIOR,
                     (P.hx("f3d9c6"), P.hx("c0392b"))),
}


def register(reg):
    for key, (draw, lk, pal, bg) in ROSTER.items():
        reg.spec(key, "Character", size=(512, 1024), height=CANVAS_M, pivot=(0.5, 0.05), shadow=True)
        pkey = "portrait_" + key.split("_", 1)[1]
        reg.spec(pkey, "Portrait", size=(256, 256), height=1.0, pivot=(0.5, 0.5))
        reg.job([key, pkey], char_job, key, draw, Look(**lk), pal, bg)
