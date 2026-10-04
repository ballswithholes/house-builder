"""Icons: white glyph silhouettes on transparent 128x128 (readable at ~40 px) + class crests.

Each glyph is drawn with a tiny vector DSL on a 0..100 design square, supersampled 4x. Cut-outs (value 0)
give internal detail while keeping one bold white silhouette. The UI composes glyphs on a school-coloured
frame and may tint them.
"""
import math

import numpy as np
from PIL import Image, ImageDraw

from brushes import catmull, stroke as _stroke
from keys import glyph_names

SIZE = 128
SS = 4
N = SIZE * SS
R = math.radians


class G:
    def __init__(self):
        self.im = Image.new("L", (N, N), 0)
        self.d = ImageDraw.Draw(self.im)
        self.tf = [(1.0, 50.0, 50.0)]

    # --- transform -----------------------------------------------------------------
    def push(self, s, cx, cy):
        self.tf.append((s, cx, cy))

    def pop(self):
        self.tf.pop()

    def T(self, x, y):
        for s, cx, cy in reversed(self.tf[1:]):
            x, y = cx + (x - 50) * s, cy + (y - 50) * s
        return (x * N / 100.0, y * N / 100.0)

    def W(self, w):
        s = 1.0
        for k, _, _ in self.tf[1:]:
            s *= k
        return w * s * N / 100.0

    # --- primitives ----------------------------------------------------------------
    def poly(self, pts, v=255, smooth=False, n=6):
        pts = np.asarray(pts, dtype=float)
        if smooth:
            pts = catmull(pts, True, n)
        self.d.polygon([self.T(x, y) for x, y in pts], fill=v)

    def circle(self, x, y, r, v=255):
        cx, cy = self.T(x, y)
        rr = self.W(r)
        self.d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=v)

    def ell(self, x, y, rx, ry, rot=0.0, v=255):
        t = np.linspace(0, 2 * math.pi, 64, endpoint=False)
        c, s = math.cos(rot), math.sin(rot)
        pts = [(x + rx * math.cos(a) * c - ry * math.sin(a) * s, y + rx * math.cos(a) * s + ry * math.sin(a) * c) for a in t]
        self.poly(pts, v)

    def line(self, pts, w, v=255, cap=True):
        P = [self.T(x, y) for x, y in pts]
        ww = max(1, int(round(self.W(w))))
        self.d.line(P, fill=v, width=ww, joint="curve")
        if cap:
            for (x, y) in (P[0], P[-1]):
                r = ww / 2.0
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=v)

    def stroke(self, pts, w0, w1=None, v=255, n=8):
        poly = _stroke(np.asarray(pts, float), w0, w0 if w1 is None else w1, n=n)
        self.poly(poly, v)

    def ring(self, x, y, r, w, v=255):
        self.circle(x, y, r, v)
        self.circle(x, y, r - w, 0 if v else 255)

    def arc(self, x, y, r, a0, a1, w, v=255, n=40, ry=None):
        ry = r if ry is None else ry
        pts = [(x + math.cos(a) * r, y + math.sin(a) * ry) for a in np.linspace(a0, a1, n)]
        self.line(pts, w, v)

    def rect(self, x0, y0, x1, y1, v=255, rad=0.0):
        a, b = self.T(x0, y0), self.T(x1, y1)
        self.d.rounded_rectangle([min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])], radius=self.W(rad), fill=v)

    def star(self, x, y, r1, r2, n=5, rot=-math.pi / 2, v=255):
        pts = []
        for k in range(n * 2):
            a = rot + k * math.pi / n
            rr = r1 if k % 2 == 0 else r2
            pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
        self.poly(pts, v)

    def rpoly(self, pts, ang, cx, cy, v=255, smooth=False):
        c, s = math.cos(ang), math.sin(ang)
        self.poly([(cx + px * c - py * s, cy + px * s + py * c) for px, py in pts], v, smooth=smooth)

    # --- composite shapes ----------------------------------------------------------
    def sword(self, cx, cy, ang, L=90, w=12, v=255):
        """Sword centred at (cx,cy), pointing along ang (0 = up). v=0 cuts a gap (for crossed weapons)."""
        a = ang
        blade = [(-w / 2, L * 0.22), (-w / 2, -L * 0.34), (0, -L * 0.5), (w / 2, -L * 0.34), (w / 2, L * 0.22)]
        guard = [(-w * 1.7, L * 0.2), (w * 1.7, L * 0.2), (w * 1.7, L * 0.29), (-w * 1.7, L * 0.29)]
        grip = [(-w * 0.32, L * 0.29), (w * 0.32, L * 0.29), (w * 0.32, L * 0.44), (-w * 0.32, L * 0.44)]
        self.rpoly(blade, a, cx, cy, v)
        self.rpoly(guard, a, cx, cy, v)
        self.rpoly(grip, a, cx, cy, v)
        c, s = math.cos(a), math.sin(a)
        self.circle(cx - s * L * 0.49, cy + c * L * 0.49, w * 0.55, v)
        if v:
            self.rpoly([(-w * 0.09, L * 0.14), (-w * 0.09, -L * 0.3), (w * 0.09, -L * 0.3), (w * 0.09, L * 0.14)], a, cx, cy, 0)

    def flame(self, cx, cy, s=1.0, v=255, inner=True):
        pts = [(0, 40), (-26, 22), (-30, -2), (-18, -24), (-12, -8), (-6, -38), (8, -18), (16, -46), (26, -14), (30, 10), (24, 30)]
        self.poly([(cx + x * s, cy + y * s) for x, y in pts], v, smooth=True, n=6)
        if inner:
            ip = [(0, 34), (-14, 22), (-14, 6), (-6, -10), (0, 4), (8, -16), (14, 4), (14, 22)]
            self.poly([(cx + x * s, cy + y * s) for x, y in ip], 0 if v else 255, smooth=True, n=6)

    def drop(self, cx, cy, s=1.0, v=255):
        pts = [(0, -40), (14, -16), (26, 6), (24, 24), (12, 36), (0, 40), (-12, 36), (-24, 24), (-26, 6), (-14, -16)]
        self.poly([(cx + x * s, cy + y * s) for x, y in pts], v, smooth=True, n=6)

    def leaf(self, cx, cy, L, ang, w=0.42, v=255, vein=True):
        pts = [(0, 0), (L * 0.3, -L * w * 0.5), (L * 0.7, -L * w * 0.45), (L, 0), (L * 0.7, L * w * 0.45), (L * 0.3, L * w * 0.5)]
        c, s = math.cos(ang), math.sin(ang)
        P = [(cx + x * c - y * s, cy + x * s + y * c) for x, y in pts]
        self.poly(P, v, smooth=True, n=5)
        if vein:
            self.line([(cx + L * 0.12 * c, cy + L * 0.12 * s), (cx + L * 0.8 * c, cy + L * 0.8 * s)], max(1.5, L * 0.05), 0 if v else 255)

    def arrow(self, x0, y0, x1, y1, w=5, head=13, fletch=True, v=255):
        a = math.atan2(y1 - y0, x1 - x0)
        c, s = math.cos(a), math.sin(a)
        self.line([(x0, y0), (x1 - c * head * 0.6, y1 - s * head * 0.6)], w, v)
        self.poly([(x1, y1), (x1 - c * head - s * head * 0.55, y1 - s * head + c * head * 0.55),
                   (x1 - c * head * 0.7, y1 - s * head * 0.7), (x1 - c * head + s * head * 0.55, y1 - s * head - c * head * 0.55)], v)
        if fletch:
            for k in (0, 1):
                bx, by = x0 + c * (4 + k * 7), y0 + s * (4 + k * 7)
                self.poly([(bx, by), (bx - c * 8 - s * 8, by - s * 8 + c * 8), (bx - c * 3 - s * 8, by - s * 3 + c * 8), (bx + c * 4, by + s * 4)], v)
                self.poly([(bx, by), (bx - c * 8 + s * 8, by - s * 8 - c * 8), (bx - c * 3 + s * 8, by - s * 3 - c * 8), (bx + c * 4, by + s * 4)], v)

    def bolt(self, cx, cy, s=1.0, v=255):
        pts = [(6, -44), (-20, 4), (-2, 4), (-10, 44), (22, -8), (4, -8), (16, -44)]
        self.poly([(cx + x * s, cy + y * s) for x, y in pts], v)

    def snowflake(self, cx, cy, r, w, v=255):
        for k in range(6):
            a = k * math.pi / 3 - math.pi / 2
            ex, ey = cx + math.cos(a) * r, cy + math.sin(a) * r
            self.line([(cx, cy), (ex, ey)], w, v)
            for t in (0.5, 0.75):
                bx, by = cx + math.cos(a) * r * t, cy + math.sin(a) * r * t
                for sgn in (-1, 1):
                    b = a + sgn * 0.75
                    self.line([(bx, by), (bx + math.cos(b) * r * 0.28, by + math.sin(b) * r * 0.28)], w * 0.8, v)
        self.circle(cx, cy, w * 1.1, v)

    def cloud(self, cx, cy, s=1.0, v=255):
        for (x, y, r) in ((-22, 6, 16), (-6, -6, 20), (14, -2, 17), (26, 10, 12), (0, 12, 16)):
            self.circle(cx + x * s, cy + y * s, r * s, v)
        self.rect(cx - 34 * s, cy + 6 * s, cx + 36 * s, cy + 22 * s, v, rad=8 * s)

    def totem_pole(self, cx, cy, s=1.0, v=255):
        self.rect(cx - 12 * s, cy - 28 * s, cx + 12 * s, cy + 40 * s, v, rad=3 * s)
        self.poly([(cx - 12 * s, cy - 18 * s), (cx - 30 * s, cy - 24 * s), (cx - 12 * s, cy - 6 * s)], v)
        self.poly([(cx + 12 * s, cy - 18 * s), (cx + 30 * s, cy - 24 * s), (cx + 12 * s, cy - 6 * s)], v)
        cut = 0 if v else 255
        for yy in (-16, 12):
            self.circle(cx - 5 * s, cy + yy * s, 2.6 * s, cut)
            self.circle(cx + 5 * s, cy + yy * s, 2.6 * s, cut)
            self.rect(cx - 6 * s, cy + (yy + 6) * s, cx + 6 * s, cy + (yy + 9) * s, cut)
        self.rect(cx - 13 * s, cy - 2 * s, cx + 13 * s, cy + 1 * s, cut)

    def hand(self, cx, cy, s=1.0, v=255, ang=0.0):
        pal = [(-16, 6), (16, 6), (18, 30), (10, 42), (-10, 42), (-18, 30)]
        fingers = [(-13, 6, -15, -22), (-4, 6, -5, -30), (5, 6, 5, -28), (13, 8, 14, -16)]
        c, sn = math.cos(ang), math.sin(ang)
        T = lambda x, y: (cx + (x * c - y * sn) * s, cy + (x * sn + y * c) * s)
        self.poly([T(x, y) for x, y in pal], v, smooth=True, n=4)
        for x0, y0, x1, y1 in fingers:
            self.line([T(x0, y0), T(x1, y1)], 8 * s, v)
        self.line([T(-16, 22), T(-30, 6)], 8 * s, v)

    def boot(self, cx, cy, s=1.0, v=255):
        pts = [(-14, -36), (10, -36), (10, 10), (30, 18), (34, 30), (30, 36), (-16, 36), (-16, 10)]
        self.poly([(cx + x * s, cy + y * s) for x, y in pts], v, smooth=False)
        self.rect(cx - 18 * s, cy - 40 * s, cx + 14 * s, cy - 30 * s, v, rad=2 * s)

    def shield_shape(self, cx, cy, s=1.0, v=255):
        pts = [(-32, -38), (0, -44), (32, -38), (32, -4), (20, 22), (0, 42), (-20, 22), (-32, -4)]
        self.poly([(cx + x * s, cy + y * s) for x, y in pts], v, smooth=True, n=5)

    def flask(self, cx, cy, s=1.0, v=255, tall=False):
        if tall:
            self.rect(cx - 7 * s, cy - 40 * s, cx + 7 * s, cy - 22 * s, v, rad=2 * s)
            self.poly([(cx - 7 * s, cy - 24 * s), (cx + 7 * s, cy - 24 * s), (cx + 22 * s, cy + 30 * s), (cx + 16 * s, cy + 40 * s), (cx - 16 * s, cy + 40 * s),
                       (cx - 22 * s, cy + 30 * s)], v, smooth=False)
        else:
            self.rect(cx - 8 * s, cy - 40 * s, cx + 8 * s, cy - 14 * s, v, rad=2 * s)
            self.circle(cx, cy + 12 * s, 28 * s, v)
        self.rect(cx - 11 * s, cy - 44 * s, cx + 11 * s, cy - 36 * s, v, rad=2 * s)
        cut = 0 if v else 255
        self.arc(cx, cy + 12 * s if not tall else cy + 14 * s, 18 * s, R(200), R(250), 4 * s, cut)

    def render(self):
        img = self.im.resize((SIZE, SIZE), Image.LANCZOS)
        a = np.asarray(img, dtype=np.float32) / 255.0
        a = np.clip(a, 0, 1)
        out = np.dstack([np.full(a.shape, 255, np.uint8)] * 3 + [np.round(a * 255).astype(np.uint8)])
        return Image.fromarray(out, "RGBA")


# =====================================================================================
# glyphs (0..100 design square)
# =====================================================================================
def g_sword(g): g.sword(50, 50, R(45), 96, 13)
def g_swords(g):
    g.sword(50, 50, R(-45), 92, 12)
    g.sword(50, 50, R(45), 100, 18, v=0)
    g.sword(50, 50, R(45), 92, 12)
def g_axe(g):
    g.line([(22, 88), (66, 18)], 8)
    g.poly([(58, 14), (86, 22), (94, 46), (82, 60), (70, 46), (56, 36)], smooth=True)
    g.poly([(60, 26), (54, 36), (46, 30), (52, 20)])
def g_mace(g):
    g.line([(24, 86), (58, 42)], 8)
    g.circle(64, 34, 17)
    for k in range(8):
        a = k * math.pi / 4
        g.poly([(64 + math.cos(a - 0.25) * 15, 34 + math.sin(a - 0.25) * 15), (64 + math.cos(a) * 27, 34 + math.sin(a) * 27),
                (64 + math.cos(a + 0.25) * 15, 34 + math.sin(a + 0.25) * 15)])
    g.rect(18, 82, 30, 94, rad=3)
def g_hammer(g):
    g.line([(24, 88), (58, 38)], 8)
    g.rpoly([(-24, -13), (24, -13), (24, 13), (-24, 13)], R(-56), 62, 30)
    g.rpoly([(-27, -15), (-20, -15), (-20, 15), (-27, 15)], R(-56), 62, 30, v=255)
    g.rpoly([(-2, -14), (2, -14), (2, 14), (-2, 14)], R(-56), 62, 30, v=0)
def g_dagger(g): g.sword(50, 52, R(35), 74, 14)
def g_daggers(g):
    g.sword(42, 50, R(-30), 70, 12)
    g.sword(58, 50, R(30), 76, 18, v=0)
    g.sword(58, 50, R(30), 70, 12)
def g_spear(g):
    g.line([(16, 90), (70, 30)], 6)
    g.rpoly([(0, -26), (9, -6), (0, 6), (-9, -6)], R(42), 76, 22)
    g.rpoly([(-11, -2), (11, -2), (11, 3), (-11, 3)], R(42), 68, 32)
def g_staff(g):
    g.line([(50, 94), (50, 36)], 8)
    g.arc(50, 24, 16, R(120), R(420), 6)
    g.circle(50, 24, 9)
    g.rect(42, 36, 58, 42, rad=2)
def g_shield(g):
    g.shield_shape(50, 50, 1.05)
    g.shield_shape(50, 50, 0.8, v=0)
    g.shield_shape(50, 50, 0.68)
    g.rect(46, 18, 54, 82, v=0)
    g.rect(28, 36, 72, 44, v=0)
def g_shield_bash(g):
    g.shield_shape(40, 52, 0.85)
    g.shield_shape(40, 52, 0.62, v=0)
    g.shield_shape(40, 52, 0.5)
    for k, (y, L) in enumerate(((26, 16), (50, 22), (74, 16))):
        g.line([(74, y), (74 + L, y + (k - 1) * 8)], 6)
def g_bow(g):
    pts = [(40, 8), (22, 26), (16, 50), (22, 74), (40, 92)]
    g.stroke(pts, 9, 9)
    g.line([(40, 8), (40, 92)], 2.6)
    g.arrow(22, 50, 92, 50, w=4.5, head=14)
def g_arrow(g): g.arrow(14, 86, 86, 14, w=6, head=18)
def g_arrows(g):
    for dx in (-18, 0, 18):
        g.arrow(40 + dx, 90, 58 + dx, 14, w=5, head=14)
def g_multishot(g):
    for a in (-35, 0, 35):
        rr = R(a - 90)
        g.arrow(50 - math.cos(rr) * 8, 88, 50 + math.cos(rr) * 46, 88 + math.sin(rr) * 70, w=5, head=14)
def g_gun(g):
    g.rect(28, 30, 92, 42, rad=3)
    g.rect(86, 26, 94, 46, rad=2)
    g.poly([(30, 34), (44, 36), (38, 56), (30, 80), (14, 78), (20, 52)], smooth=True)
    g.arc(46, 46, 9, R(0), R(180), 4)
    g.rect(20, 26, 30, 32, rad=2)
def g_trap(g):
    g.rect(14, 70, 86, 80, rad=3)
    for s in (-1, 1):
        g.arc(50, 70, 32, R(180) if s < 0 else R(270), R(270) if s < 0 else R(360), 8)
    for k in range(7):
        x = 22 + k * 9.3
        y = 70 - math.sqrt(max(0, 32 ** 2 - (x - 50) ** 2)) + 6
        g.poly([(x - 4, y), (x + 4, y), (x, y + 12)])
    g.circle(50, 84, 6)
    g.line([(50, 88), (50, 96)], 4)
def g_paw(g):
    g.poly([(30, 62), (50, 46), (70, 62), (74, 80), (60, 90), (50, 86), (40, 90), (26, 80)], smooth=True)
    for (x, y, r) in ((22, 42, 9), (38, 26, 10), (62, 26, 10), (78, 42, 9)):
        g.ell(x, y, r, r * 1.2)
def g_claw(g):
    for dx in (-22, 0, 22):
        g.stroke([(30 + dx, 10), (44 + dx, 40), (52 + dx, 90)], 3, 13)
def g_fang(g):
    g.arc(50, 6, 40, R(30), R(150), 10)
    for s in (-1, 1):
        g.stroke([(50 + s * 18, 30), (50 + s * 22, 58), (50 + s * 12, 90)], 22, 1)
def g_wolf(g):
    g.poly([(18, 84), (22, 54), (30, 30), (34, 8), (46, 28), (58, 24), (66, 8), (68, 32), (90, 52), (92, 62), (70, 66), (60, 84)], smooth=False)
    g.poly([(64, 42), (72, 44), (66, 48)], v=0)
    g.poly([(88, 52), (94, 56), (90, 60)], v=0)
def g_eagle(g):
    for s in (-1, 1):
        g.poly([(50, 40), (50 + s * 20, 28), (50 + s * 46, 16), (50 + s * 44, 26), (50 + s * 48, 30), (50 + s * 42, 38), (50 + s * 44, 44),
                (50 + s * 34, 50), (50 + s * 16, 52)], smooth=False)
    g.ell(50, 56, 11, 22)
    g.circle(50, 32, 9)
    g.poly([(50, 28), (62, 32), (54, 38)])
    g.poly([(40, 74), (60, 74), (56, 92), (50, 86), (44, 92)])
def g_hawk(g):
    g.poly([(16, 90), (20, 56), (34, 30), (56, 18), (78, 22), (90, 36), (86, 50), (76, 44), (70, 50), (62, 50), (58, 66), (44, 90)], smooth=True)
    g.poly([(76, 44), (92, 38), (90, 56), (82, 54)])
    g.circle(62, 32, 5, v=0)
    g.circle(63, 32, 2)
def g_monkey(g):
    g.circle(18, 48, 12)
    g.circle(82, 48, 12)
    g.circle(50, 48, 34)
    g.circle(18, 48, 6, v=0)
    g.circle(82, 48, 6, v=0)
    g.poly([(30, 42), (42, 30), (50, 40), (58, 30), (70, 42), (72, 64), (50, 78), (28, 64)], v=0, smooth=True)
    g.poly([(34, 46), (44, 38), (50, 46), (56, 38), (66, 46), (66, 62), (50, 72), (34, 62)], smooth=True)
    g.circle(42, 47, 4, v=0)
    g.circle(58, 47, 4, v=0)
    g.arc(50, 58, 8, R(20), R(160), 3, v=0)
def g_cheetah(g):
    g.poly([(20, 24), (30, 36), (70, 36), (80, 24), (84, 50), (74, 76), (50, 88), (26, 76), (16, 50)], smooth=True)
    for x in (38, 62):
        g.ell(x, 52, 6, 4, v=0)
        g.line([(x + (-3 if x < 50 else 3), 56), (x + (-6 if x < 50 else 6), 74)], 3, v=0)
    g.poly([(44, 66), (56, 66), (50, 72)], v=0)
    for (x, y) in ((30, 44), (70, 44), (50, 40), (36, 66), (64, 66)):
        g.circle(x, y, 2.4, v=0)
def g_turtle(g):
    g.circle(50, 16, 9)
    for (x, y) in ((22, 34), (78, 34), (24, 74), (76, 74)):
        g.ell(x, y, 10, 7, rot=R(-30 if (x < 50) == (y < 50) else 30))
    g.ell(50, 54, 30, 34)
    g.poly([(50, 36), (64, 46), (60, 64), (40, 64), (36, 46)], v=0)
    g.poly([(50, 41), (59, 48), (56, 60), (44, 60), (41, 48)])
    g.line([(50, 84), (52, 94)], 6)
def g_snake(g):
    g.stroke([(20, 86), (66, 78), (72, 58), (34, 50), (30, 30), (64, 22)], 7, 14)
    g.ell(70, 22, 12, 9, rot=R(-10))
    g.circle(73, 19, 2.5, v=0)
    g.line([(81, 24), (90, 26)], 2.5)
    g.line([(90, 26), (95, 22)], 2)
    g.line([(90, 26), (95, 30)], 2)
def g_scorpion(g):
    for k in range(3):
        g.ell(50, 58 + k * 7, 13 - k * 1.5, 7)
    g.stroke([(50, 52), (52, 34), (62, 18), (76, 16), (80, 26)], 10, 6)
    g.poly([(80, 24), (88, 32), (76, 32)])
    for s in (-1, 1):
        g.line([(50 + s * 10, 54), (50 + s * 26, 44), (50 + s * 30, 30)], 5)
        g.poly([(50 + s * 24, 30), (50 + s * 36, 20), (50 + s * 38, 34)])
        for k in range(3):
            g.line([(50 + s * 10, 62 + k * 6), (50 + s * 28, 70 + k * 8), (50 + s * 32, 84 + k * 4)], 3)
def g_spider(g):
    for s in (-1, 1):
        for k in range(4):
            y = 40 + k * 7
            g.line([(50, y), (50 + s * 24, y - 18 + k * 8), (50 + s * 40, y + 10 + k * 6)], 4.2)
    g.ell(50, 64, 16, 20)
    g.circle(50, 38, 11)
    g.circle(46, 36, 2.5, v=0)
    g.circle(54, 36, 2.5, v=0)
def g_owl(g):
    g.poly([(26, 20), (40, 30), (60, 30), (74, 20), (78, 50), (74, 78), (50, 92), (26, 78), (22, 50)], smooth=True)
    for x in (38, 62):
        g.circle(x, 44, 11, v=0)
        g.circle(x, 44, 5)
    g.poly([(46, 54), (54, 54), (50, 64)], v=0)
    for k in range(3):
        g.arc(50, 74 + k * 0, 6 + k * 6, R(30), R(150), 2.5, v=0) if False else None
def g_boar(g):
    g.poly([(14, 22), (30, 30), (70, 30), (86, 22), (82, 46), (76, 70), (64, 88), (36, 88), (24, 70), (18, 46)], smooth=True)
    g.ell(50, 74, 14, 11, v=0)
    g.ell(50, 74, 11, 8)
    g.circle(45, 74, 2.6, v=0)
    g.circle(55, 74, 2.6, v=0)
    g.circle(36, 50, 4, v=0)
    g.circle(64, 50, 4, v=0)
    for s in (-1, 1):
        g.stroke([(50 + s * 18, 78), (50 + s * 26, 70), (50 + s * 28, 56)], 6, 1)
def g_bear(g):
    g.circle(24, 26, 12)
    g.circle(76, 26, 12)
    g.circle(50, 54, 36)
    g.circle(24, 26, 5, v=0)
    g.circle(76, 26, 5, v=0)
    g.ell(50, 66, 16, 12, v=0)
    g.ell(50, 66, 12, 9)
    g.ell(50, 61, 6, 4, v=0)
    g.circle(36, 46, 4, v=0)
    g.circle(64, 46, 4, v=0)
def g_fire(g): g.flame(50, 52, 1.08)
def g_fireball(g):
    g.poly([(66, 26), (40, 40), (12, 86), (30, 62), (34, 74), (48, 54), (56, 66), (74, 46)], smooth=True)
    g.circle(66, 34, 22)
    g.circle(66, 34, 11, v=0)
    g.circle(68, 32, 5)
def g_flame_wave(g):
    g.arc(50, 140, 70, R(235), R(305), 10)
    for (x, s) in ((24, 0.55), (50, 0.72), (76, 0.55)):
        g.flame(x, 64 - (12 if x == 50 else 0), s)
def g_meteor(g):
    for k, (y, L) in enumerate(((26, 40), (42, 50), (58, 36))):
        g.line([(10 + k * 6, y - 14 + k * 4), (10 + L, y + 14)], 6)
    g.circle(64, 62, 24)
    g.circle(58, 56, 6, v=0)
    g.circle(72, 70, 4, v=0)
    g.circle(70, 52, 3, v=0)
def g_ember(g):
    g.flame(48, 62, 0.75)
    for (x, y, r) in ((70, 24, 4), (30, 18, 3), (58, 10, 3)):
        g.circle(x, y, r)
def g_frost(g):
    for (x, y, a, L) in ((50, 86, 0, 76), (40, 88, -0.5, 52), (62, 88, 0.45, 56)):
        g.rpoly([(0, 0), (-8, -L * 0.25), (0, -L), (8, -L * 0.25)], a, x, y)
        g.rpoly([(0, -L * 0.2), (0, -L * 0.85), (2.5, -L * 0.2)], a, x, y, v=0)
def g_snowflake(g): g.snowflake(50, 50, 42, 6.5)
def g_ice_shard(g):
    g.rpoly([(0, -46), (14, -10), (6, 44), (-6, 44), (-14, -10)], R(25), 50, 50)
    g.rpoly([(0, -38), (3, -8), (0, 36), (-1.5, -8)], R(25), 50, 50, v=0)
def g_ice_block(g):
    g.poly([(50, 10), (88, 30), (88, 70), (50, 90), (12, 70), (12, 30)])
    g.line([(12, 30), (50, 50), (88, 30)], 3.5, v=0)
    g.line([(50, 50), (50, 90)], 3.5, v=0)
    g.line([(24, 44), (30, 60)], 2.5, v=0)
def g_frost_nova(g):
    for k in range(12):
        a = k * math.pi / 6
        g.poly([(50 + math.cos(a - 0.12) * 30, 50 + math.sin(a - 0.12) * 30), (50 + math.cos(a) * 48, 50 + math.sin(a) * 48),
                (50 + math.cos(a + 0.12) * 30, 50 + math.sin(a + 0.12) * 30)])
    g.ring(50, 50, 32, 6)
    g.snowflake(50, 50, 18, 4.5)
def g_arcane(g):
    g.star(50, 50, 46, 12, n=4, rot=-math.pi / 2)
    g.star(50, 50, 30, 9, n=4, rot=-math.pi / 4)
    g.ring(50, 50, 24, 4, v=0)
    g.circle(50, 50, 6)
def g_arcane_orb(g):
    g.circle(50, 50, 26)
    g.circle(42, 42, 7, v=0)
    g.ell(50, 50, 46, 14, rot=R(-25))
    g.ell(50, 50, 40, 8, rot=R(-25), v=0)
    g.circle(50, 50, 26)
    g.circle(42, 42, 6, v=0)
    g.arc(50, 50, 43, R(-25) + 0.05 + math.pi * 0.0, R(-25) + math.pi * 0.95, 6, ry=11) if False else None
def g_missiles(g):
    for (x, y) in ((70, 22), (78, 52), (60, 78)):
        g.line([(x - 40, y + 10), (x, y)], 4)
        g.star(x, y, 13, 4, n=4, rot=0.2)
def g_portal(g):
    g.ell(50, 50, 30, 44)
    g.ell(50, 50, 22, 36, v=0)
    pts = [(50 + math.cos(t) * (2 + t * 2.4) * 0.9, 50 + math.sin(t) * (2 + t * 2.4) * 1.3) for t in np.linspace(0, 4 * math.pi, 60)]
    g.line(pts, 4.5)
def g_blink(g):
    for dx in (0, 22):
        g.line([(16 + dx, 22), (40 + dx, 50), (16 + dx, 78)], 9)
    g.star(78, 50, 20, 5, n=4)
def g_sheep(g):
    for (x, y, r) in ((36, 44, 16), (52, 38, 17), (68, 44, 15), (44, 58, 16), (62, 58, 16), (28, 56, 12)):
        g.circle(x, y, r)
    g.ell(80, 52, 10, 13, rot=R(15))
    g.circle(83, 49, 2.2, v=0)
    for x in (36, 48, 58, 68):
        g.line([(x, 68), (x, 88)], 6)
def g_brain(g):
    g.ell(36, 50, 28, 32)
    g.ell(64, 50, 28, 32)
    g.line([(50, 18), (50, 82)], 3.5, v=0)
    for pts in ([(20, 40), (30, 36), (36, 44)], [(22, 62), (34, 58), (40, 66)], [(62, 32), (72, 38), (80, 34)], [(60, 56), (70, 52), (78, 60)],
                [(30, 24), (40, 28)], [(64, 74), (74, 70)]):
        g.line(pts, 3.2, v=0)
def g_eye(g):
    g.poly([(6, 50), (28, 28), (50, 22), (72, 28), (94, 50), (72, 72), (50, 78), (28, 72)], smooth=True)
    g.circle(50, 50, 19, v=0)
    g.circle(50, 50, 14)
    g.circle(50, 50, 6, v=0)
    g.circle(44, 44, 3)
def g_holy(g):
    for k in range(16):
        a = k * math.pi / 8
        L = 48 if k % 2 == 0 else 36
        g.poly([(50 + math.cos(a - 0.1) * 20, 50 + math.sin(a - 0.1) * 20), (50 + math.cos(a) * L, 50 + math.sin(a) * L),
                (50 + math.cos(a + 0.1) * 20, 50 + math.sin(a + 0.1) * 20)])
    g.circle(50, 50, 24)
    g.rect(46, 34, 54, 66, v=0)
    g.rect(38, 42, 62, 50, v=0)
def g_cross(g):
    g.rect(40, 8, 60, 92, rad=3)
    g.rect(18, 26, 82, 46, rad=3)
def g_sun(g):
    for k in range(8):
        a = k * math.pi / 4
        g.poly([(50 + math.cos(a - 0.2) * 26, 50 + math.sin(a - 0.2) * 26), (50 + math.cos(a) * 47, 50 + math.sin(a) * 47),
                (50 + math.cos(a + 0.2) * 26, 50 + math.sin(a + 0.2) * 26)])
    g.circle(50, 50, 24)
    g.circle(50, 50, 16, v=0)
    g.circle(50, 50, 12)
def g_halo(g):
    g.ell(50, 26, 34, 12)
    g.ell(50, 26, 26, 6, v=0)
    g.circle(50, 64, 18)
    g.poly([(24, 96), (32, 80), (50, 76), (68, 80), (76, 96)], smooth=False)
def g_wings(g):
    for s in (-1, 1):
        for k in range(4):
            y = 30 + k * 12
            L = 42 - k * 7
            g.poly([(50 + s * 4, y - 4), (50 + s * (8 + L), y - 14 + k * 2), (50 + s * (6 + L * 0.9), y + 6), (50 + s * 4, y + 8)], smooth=True)
def g_hand(g): g.hand(50, 50, 1.05)
def g_hands_pray(g):
    g.poly([(50, 6), (64, 36), (68, 70), (60, 92), (40, 92), (32, 70), (36, 36)], smooth=True)
    g.line([(50, 14), (50, 88)], 3, v=0)
    g.rect(26, 70, 74, 80, rad=4)
    g.rect(28, 72, 72, 74, v=0)
def g_heart(g):
    g.circle(34, 38, 20)
    g.circle(66, 38, 20)
    g.poly([(15, 44), (50, 88), (85, 44), (50, 40)])
def g_heal_plus(g):
    g.rect(38, 10, 62, 90, rad=6)
    g.rect(10, 38, 90, 62, rad=6)
def g_renew(g):
    g.arc(50, 50, 36, R(-60), R(220), 9)
    g.poly([(66, 8), (84, 26), (60, 30)])
    g.leaf(36, 64, 32, R(-45), 0.5)
def g_shadow(g):
    g.circle(50, 50, 40)
    g.circle(64, 40, 34, v=0)
    for k in range(3):
        g.arc(56, 60, 10 + k * 9, R(200), R(300), 3.5)
def g_skull(g):
    g.poly([(18, 44), (24, 20), (50, 10), (76, 20), (82, 44), (72, 62), (70, 80), (30, 80), (28, 62)], smooth=True)
    g.ell(37, 46, 10, 11, v=0)
    g.ell(63, 46, 10, 11, v=0)
    g.poly([(50, 56), (56, 66), (44, 66)], v=0)
    for x in (38, 46, 54, 62):
        g.rect(x - 2, 74, x + 2, 84, v=0)
    g.rect(30, 84, 70, 92, rad=3)
def g_moon(g):
    g.circle(46, 50, 40)
    g.circle(64, 40, 34, v=0)
    g.star(78, 70, 9, 3, n=4)
def g_void(g):
    pts = [(50 + math.cos(t) * (3 + t * 3.3), 50 + math.sin(t) * (3 + t * 3.3)) for t in np.linspace(0, 3.6 * math.pi, 80)]
    g.stroke(pts, 4, 12, n=3)
    g.circle(50, 50, 6)
def g_tentacle(g):
    pts = [(40, 94), (34, 66), (44, 40), (66, 28), (74, 44), (60, 52), (56, 42)]
    g.stroke(pts, 22, 4, n=8)
    for (x, y, r) in ((38, 76, 3.5), (37, 62, 3.2), (42, 48, 3), (52, 38, 2.6)):
        g.circle(x + 4, y, r, v=0)
def g_demon(g):
    for s in (-1, 1):
        g.stroke([(50 + s * 18, 34), (50 + s * 34, 20), (50 + s * 38, 4)], 14, 1)
    g.poly([(24, 40), (50, 28), (76, 40), (72, 70), (50, 92), (28, 70)], smooth=True)
    for s in (-1, 1):
        g.poly([(50 + s * 6, 52), (50 + s * 22, 46), (50 + s * 18, 58)], v=0)
    g.poly([(38, 72), (62, 72), (50, 80)], v=0)
def g_imp(g):
    for s in (-1, 1):
        g.poly([(50 + s * 22, 46), (50 + s * 48, 30), (50 + s * 28, 60)])
        g.stroke([(50 + s * 10, 30), (50 + s * 16, 12)], 8, 1)
    g.circle(50, 56, 26)
    for s in (-1, 1):
        g.ell(50 + s * 10, 52, 5, 6, v=0)
    g.poly([(36, 66), (64, 66), (50, 76)], v=0)
def g_curse(g):
    g.poly([(8, 46), (30, 28), (50, 24), (70, 28), (92, 46), (70, 64), (50, 68), (30, 64)], smooth=True)
    g.circle(50, 46, 13, v=0)
    g.ell(50, 46, 4, 11)
    for x, L in ((32, 22), (50, 30), (68, 20)):
        g.stroke([(x, 62), (x, 62 + L)], 7, 7)
        g.circle(x, 64 + L, 5)
    for s in (-1, 1):
        g.poly([(50 + s * 20, 24), (50 + s * 28, 8), (50 + s * 30, 26)])
def g_fear(g):
    g.poly([(22, 90), (20, 40), (32, 16), (50, 8), (68, 16), (80, 40), (78, 90), (68, 82), (58, 92), (50, 82), (42, 92), (32, 82)], smooth=True)
    g.ell(38, 40, 7, 10, v=0)
    g.ell(62, 40, 7, 10, v=0)
    g.ell(50, 66, 10, 14, v=0)
def g_drain(g):
    g.drop(50, 56, 0.85)
    g.drop(50, 58, 0.45, v=0)
    for s in (-1, 1):
        g.arc(50 + s * 30, 30, 18, R(90 - 90 * s), R(180 - 90 * s) if s > 0 else R(90), 4.5) if False else None
        g.line([(50 + s * 44, 10), (50 + s * 40, 28), (50 + s * 30, 40)], 5)
        g.circle(50 + s * 44, 10, 4)
def g_soul_shard(g):
    g.poly([(50, 6), (74, 30), (66, 84), (50, 96), (34, 84), (26, 30)])
    g.line([(26, 30), (50, 40), (74, 30)], 3, v=0)
    g.line([(50, 40), (50, 92)], 3, v=0)
    g.poly([(40, 36), (47, 30), (47, 70)], v=0)
def g_nature(g):
    g.stroke([(50, 94), (50, 60), (52, 40)], 7, 5)
    g.leaf(50, 60, 38, R(-150), 0.55)
    g.leaf(52, 46, 42, R(-30), 0.55)
def g_leaf(g):
    g.leaf(14, 86, 92, R(-45), 0.5)
    for k in range(4):
        t = 0.25 + k * 0.16
        x, y = 14 + 92 * t * 0.707, 86 - 92 * t * 0.707
        g.line([(x, y), (x + 10, y + 2)], 2.5, v=0)
        g.line([(x, y), (x - 2, y - 10)], 2.5, v=0)
def g_lightning(g): g.bolt(50, 50, 1.05)
def g_chain_lightning(g):
    g.line([(16, 10), (36, 36), (26, 44), (52, 70)], 7)
    g.line([(52, 70), (70, 56), (64, 76), (90, 92)], 6)
    g.line([(36, 36), (62, 24), (60, 36), (88, 26)], 5)
    for (x, y) in ((16, 10), (88, 26), (90, 92)):
        g.circle(x, y, 6)
def g_storm(g):
    g.cloud(50, 32, 1.05)
    g.bolt(50, 72, 0.55)
def g_earth(g):
    g.poly([(4, 86), (36, 24), (54, 54), (66, 38), (96, 86)])
    g.poly([(36, 24), (46, 44), (40, 40), (34, 48), (28, 38)], v=0)
    g.poly([(66, 38), (74, 52), (68, 50), (62, 54)], v=0)
def g_rock(g):
    g.poly([(10, 78), (16, 44), (36, 22), (64, 20), (86, 40), (92, 76), (70, 88), (30, 88)])
    g.line([(36, 22), (44, 50), (16, 44)], 3, v=0)
    g.line([(44, 50), (70, 56), (86, 40)], 3, v=0)
    g.line([(70, 56), (70, 88)], 3, v=0)
def g_wave(g):
    pts = [(4, 80), (20, 70), (32, 46), (50, 26), (72, 22), (88, 34), (84, 50), (70, 46), (64, 56), (72, 70), (96, 74), (96, 92), (4, 92)]
    g.poly(pts, smooth=True, n=6)
    g.arc(70, 40, 10, R(90), R(330), 3.5, v=0)
def g_water_drop(g):
    g.drop(50, 52, 1.08)
    g.arc(50, 58, 20, R(110), R(170), 5, v=0)
def g_totem(g): g.totem_pole(50, 52, 1.15)
def g_totem_fire(g):
    g.totem_pole(50, 62, 0.9)
    g.flame(50, 18, 0.45)
def g_totem_earth(g):
    g.totem_pole(50, 62, 0.9)
    g.poly([(30, 30), (40, 8), (50, 22), (58, 6), (72, 30)])
def g_totem_water(g):
    g.totem_pole(50, 62, 0.9)
    g.drop(50, 16, 0.38)
def g_totem_air(g):
    g.totem_pole(50, 62, 0.9)
    for k in range(2):
        g.arc(50, 18, 8 + k * 9, R(200), R(340), 3.5)
def g_wind(g):
    g.line([(8, 30), (60, 30)], 7)
    g.arc(60, 20, 10, R(-90), R(90), 7) if False else g.arc(60, 20, 10, R(90), R(-180) + 2 * math.pi * 0 - math.pi, 7) if False else None
    pts = [(8, 30), (64, 30)] + [(64 + math.cos(a) * 12, 18 + math.sin(a) * 12) for a in np.linspace(math.pi / 2, -math.pi, 18)]
    g.line(pts, 7)
    pts = [(14, 52), (78, 52)] + [(78 + math.cos(a) * 10, 42 + math.sin(a) * 10) for a in np.linspace(math.pi / 2, -math.pi, 18)]
    g.line(pts, 7)
    pts = [(20, 74), (56, 74)] + [(56 + math.cos(a) * 10, 84 + math.sin(a) * 10) for a in np.linspace(-math.pi / 2, math.pi, 18)]
    g.line(pts, 7)
def g_wolf_spirit(g):
    g.push(0.86, 50, 56)
    g_wolf(g)
    g.pop()
    for (x, y, s) in ((24, 18, 0.25), (50, 10, 0.28), (78, 20, 0.22)):
        g.flame(x, y, s, inner=False)
def g_stealth(g):
    g.poly([(14, 94), (20, 54), (32, 24), (50, 10), (68, 24), (80, 54), (86, 94)], smooth=True)
    g.poly([(32, 50), (50, 34), (68, 50), (64, 70), (36, 70)], v=0, smooth=True)
    g.ell(42, 54, 4, 2.5)
    g.ell(58, 54, 4, 2.5)
def g_mask(g):
    g.poly([(6, 40), (24, 30), (50, 36), (76, 30), (94, 40), (88, 60), (70, 68), (50, 58), (30, 68), (12, 60)], smooth=True)
    g.ell(32, 48, 10, 6, rot=R(10), v=0)
    g.ell(68, 48, 10, 6, rot=R(-10), v=0)
def g_poison(g):
    g.drop(46, 58, 0.9)
    g.circle(46, 66, 11, v=0)
    g.circle(41, 64, 3)
    g.circle(51, 64, 3)
    g.rect(43, 72, 49, 76)
    for (x, y, r) in ((76, 26, 7), (84, 46, 5), (70, 10, 4)):
        g.ring(x, y, r, 2.5)
def g_vial(g):
    g.rect(40, 6, 60, 14, rad=2)
    g.rect(44, 12, 56, 30)
    g.poly([(44, 28), (56, 28), (64, 44), (64, 84), (56, 94), (44, 94), (36, 84), (36, 44)], smooth=True)
    g.rect(42, 50, 46, 82, v=0, rad=2)
def g_coin(g):
    g.circle(50, 50, 42)
    g.ring(50, 50, 34, 3.5, v=0)
    g.star(50, 52, 19, 8, n=5, v=0)
def g_kick(g):
    g.push(1.0, 58, 50)
    g.rpoly([(-14, -36), (10, -36), (10, 10), (30, 18), (34, 30), (30, 36), (-16, 36), (-16, 10)], R(-40), 50, 50)
    g.pop()
    for k in range(3):
        g.line([(8, 30 + k * 16), (26, 34 + k * 16)], 5)
def g_boot(g): g.boot(46, 50, 1.15)
def g_fist(g):
    g.rect(22, 30, 78, 76, rad=14)
    for k in range(4):
        g.line([(30 + k * 14, 30), (30 + k * 14, 44)], 2.5, v=0)
    g.rect(22, 46, 64, 58, rad=6, v=0)
    g.rect(24, 48, 62, 56, rad=4)
    g.rect(30, 74, 70, 92, rad=4)
def g_shout(g):
    g.poly([(10, 36), (34, 36), (56, 18), (56, 82), (34, 64), (10, 64)])
    for k in range(3):
        g.arc(56, 50, 14 + k * 11, R(-45), R(45), 5)
def g_banner(g):
    g.line([(22, 94), (22, 8)], 6)
    g.circle(22, 8, 5)
    g.poly([(24, 14), (84, 14), (84, 64), (70, 54), (54, 64), (54, 50), (24, 50)], smooth=False)
    g.star(54, 32, 10, 4, n=5, v=0)
def g_rage(g):
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        cx, cy = 50 + math.cos(a) * 22, 50 + math.sin(a) * 22
        g.arc(cx + math.cos(a) * 16, cy + math.sin(a) * 16, 18, a + math.pi - 0.75, a + math.pi + 0.75, 8)
def g_blood(g):
    g.drop(38, 44, 0.75)
    g.drop(70, 30, 0.42)
    g.drop(70, 74, 0.4)
def g_whirlwind(g):
    for k in range(6):
        y = 16 + k * 13
        r = 40 - k * 6
        g.arc(50 + (k % 2) * 4 - 2, y, r, R(10), R(170), 6, ry=r * 0.3)
        g.arc(50 + (k % 2) * 4 - 2, y, r, R(190), R(350), 4, ry=r * 0.3)
def g_charge(g):
    g.poly([(30, 34), (66, 34), (66, 18), (94, 50), (66, 82), (66, 66), (30, 66)])
    for k in range(3):
        g.line([(6, 36 + k * 14), (20, 36 + k * 14)], 5)
def g_stance_battle(g):
    g.ring(50, 50, 46, 6)
    g.push(0.78, 50, 50)
    g_swords(g)
    g.pop()
def g_stance_defensive(g):
    g.sword(50, 50, R(0), 96, 11)
    g.shield_shape(50, 56, 0.9, v=0)
    g.shield_shape(50, 56, 0.78)
    g.shield_shape(50, 56, 0.55, v=0)
    g.shield_shape(50, 56, 0.45)
def g_stance_berserker(g):
    g_axe(g)
    for (x, y, a) in ((20, 24, -2.4), (14, 50, 3.0), (34, 12, -1.9)):
        g.rpoly([(0, -4), (14, 0), (0, 4)], a, x, y)
def g_armor(g):
    g.poly([(14, 22), (34, 12), (44, 22), (56, 22), (66, 12), (86, 22), (80, 42), (74, 40), (74, 84), (50, 92), (26, 84), (26, 40), (20, 42)], smooth=False)
    g.line([(50, 24), (50, 88)], 3, v=0)
    g.arc(50, 62, 16, R(20), R(160), 3, v=0)
def g_aura(g):
    g.circle(50, 50, 12)
    for k in range(3):
        r = 22 + k * 11
        for q in range(4):
            g.arc(50, 50, r, q * math.pi / 2 + 0.25, q * math.pi / 2 + math.pi / 2 - 0.25, 5)
def g_seal(g):
    g.star(50, 50, 46, 40, n=16, rot=0)
    g.circle(50, 50, 33, v=0)
    g.circle(50, 50, 29)
    g.push(0.5, 50, 50)
    g_sun(g)
    g.pop()
    g.circle(50, 50, 6, v=0)
def g_judgement(g):
    for k in range(7):
        a = math.pi + k * math.pi / 6
        g.line([(50 + math.cos(a) * 38, 40 + math.sin(a) * 38), (50 + math.cos(a) * 48, 40 + math.sin(a) * 48)], 4)
    g.rect(28, 26, 72, 46, rad=4)
    g.line([(50, 46), (50, 92)], 8)
    g.rect(36, 88, 64, 96, rad=2)
def g_blessing(g):
    g.hand(50, 60, 0.85)
    g.star(30, 16, 10, 3, n=4)
    g.star(54, 8, 7, 2.2, n=4)
    g.star(76, 18, 9, 2.8, n=4)
def g_lock(g):
    g.arc(50, 40, 20, R(180), R(360), 9)
    g.line([(30, 40), (30, 48)], 9)
    g.line([(70, 40), (70, 48)], 9)
    g.rect(18, 46, 82, 92, rad=8)
    g.circle(50, 64, 7, v=0)
    g.rect(47, 66, 53, 80, v=0)
def g_key(g):
    g.ring(30, 34, 22, 9)
    g.line([(44, 50), (86, 88)], 9)
    g.rpoly([(0, 0), (12, 0), (12, 8), (0, 8)], R(42), 70, 70)
    g.rpoly([(0, 0), (10, 0), (10, 8), (0, 8)], R(42), 80, 80)
def g_potion_red(g):
    g.flask(50, 50, 1.0)
def g_potion_blue(g):
    g.flask(50, 50, 1.0, tall=True)
def g_food(g):
    g.ell(40, 40, 28, 24, rot=R(-35))
    g.line([(58, 58), (80, 80)], 9)
    g.circle(84, 78, 7)
    g.circle(78, 86, 7)
    g.arc(36, 34, 14, R(200), R(280), 4, v=0)
def g_drink(g):
    g.rect(20, 28, 66, 92, rad=6)
    g.ring(70, 58, 16, 7)
    g.rect(20, 28, 66, 92, rad=6)
    for (x, r) in ((26, 9), (40, 11), (56, 10)):
        g.circle(x, 26, r)
    g.rect(30, 44, 36, 82, v=0, rad=2)
    g.rect(48, 44, 54, 82, v=0, rad=2)
def g_star(g): g.star(50, 54, 46, 19, n=5)
def g_sparkle(g):
    g.star(42, 56, 38, 9, n=4, rot=0)
    g.star(78, 22, 14, 4, n=4, rot=0)
    g.star(80, 80, 9, 3, n=4, rot=0)
def g_clock(g):
    g.ring(50, 50, 44, 9)
    g.line([(50, 50), (50, 22)], 7)
    g.line([(50, 50), (70, 60)], 7)
    for k in range(12):
        a = k * math.pi / 6
        g.circle(50 + math.cos(a) * 28, 50 + math.sin(a) * 28, 2.2 if k % 3 else 3.2)
def g_hourglass(g):
    g.rect(20, 6, 80, 16, rad=3)
    g.rect(20, 84, 80, 94, rad=3)
    g.poly([(26, 16), (74, 16), (54, 50), (74, 84), (26, 84), (46, 50)])
    g.poly([(33, 22), (67, 22), (52, 46), (48, 46)], v=0)
    g.poly([(48, 56), (52, 56), (68, 78), (32, 78)], v=0)
    g.poly([(40, 70), (60, 70), (66, 78), (34, 78)])
def g_feather(g):
    pts = [(18, 92), (30, 64), (50, 34), (76, 10), (84, 18), (70, 48), (44, 72)]
    g.poly(pts, smooth=True)
    g.line([(14, 96), (78, 16)], 3.5, v=0)
    for k in range(4):
        x, y = 34 + k * 11, 70 - k * 13
        g.line([(x, y), (x + 10, y + 6)], 2.5, v=0)
def g_bandage(g):
    g.rpoly([(-42, -11), (42, -11), (42, 11), (-42, 11)], R(45), 50, 50)
    g.rpoly([(-42, -11), (42, -11), (42, 11), (-42, 11)], R(-45), 50, 50, v=0) if False else None
    g.rpoly([(-46, -15), (46, -15), (46, 15), (-46, 15)], R(-45), 50, 50, v=0)
    g.rpoly([(-42, -11), (42, -11), (42, 11), (-42, 11)], R(-45), 50, 50)
    g.rpoly([(-12, -8), (12, -8), (12, 8), (-12, 8)], R(-45), 50, 50, v=0)
    for (dx, dy) in ((-4, -4), (4, 4), (-4, 4), (4, -4)):
        g.circle(50 + dx, 50 + dy, 1.8)
def g_crown(g):
    g.poly([(10, 34), (30, 56), (50, 20), (70, 56), (90, 34), (82, 80), (18, 80)])
    g.rect(18, 80, 82, 92, rad=3)
    for (x, y) in ((10, 30), (50, 16), (90, 30)):
        g.circle(x, y, 6)
    g.circle(50, 66, 6, v=0)


# =====================================================================================
# class crests
# =====================================================================================
def crest_frame(g):
    g.circle(50, 50, 48)
    g.circle(50, 50, 42, v=0)
    for k in range(4):
        a = k * math.pi / 2
        g.rpoly([(0, -8), (6, 0), (0, 8), (-6, 0)], a, 50 + math.cos(a) * 45, 50 + math.sin(a) * 45)
    g.circle(50, 50, 38)
    g.circle(50, 50, 35, v=0)


def crest(inner):
    def fn(g):
        crest_frame(g)
        g.push(0.6, 50, 50)
        inner(g)
        g.pop()
    return fn


def c_warrior(g):
    g_swords(g)


def c_hunter(g):
    g_bow(g)


def c_paladin(g):
    g_sun(g)
    g.circle(50, 50, 16, v=0)
    g.rect(46, 30, 54, 70)
    g.rect(36, 40, 64, 48)


def c_mage(g):
    g_arcane(g)


def c_priest(g):
    g_wings(g)
    g.ell(50, 14, 18, 6)
    g.ell(50, 14, 12, 3, v=0)


def c_rogue(g):
    g_daggers(g)


def c_warlock(g):
    g_demon(g)


def c_shaman(g):
    g_totem(g)


CRESTS = {"warrior": c_warrior, "hunter": c_hunter, "paladin": c_paladin, "mage": c_mage, "priest": c_priest, "rogue": c_rogue,
          "warlock": c_warlock, "shaman": c_shaman}


def draw_glyph(name):
    fn = globals().get("g_" + name)
    if fn is None:
        raise KeyError("no glyph painter for '%s'" % name)
    g = G()
    fn(g)
    return g.render()


def draw_crest(cls):
    g = G()
    crest(CRESTS[cls])(g)
    return g.render()


def _job_glyphs(names):
    return {"glyph_" + n: draw_glyph(n) for n in names}


def _job_crests(classes):
    return {"crest_" + c: draw_crest(c) for c in classes}


def register(reg):
    names = glyph_names()
    for n in names:
        reg.spec("glyph_" + n, "Icon", size=(SIZE, SIZE), height=1.0, pivot=(0.5, 0.5))
    # batch glyphs in groups (cheap to draw)
    for i in range(0, len(names), 24):
        reg.job(["glyph_" + n for n in names[i:i + 24]], _job_glyphs, names[i:i + 24])
    for c in CRESTS:
        reg.spec("crest_" + c, "Icon", size=(SIZE, SIZE), height=1.0, pivot=(0.5, 0.5))
    reg.job(["crest_" + c for c in CRESTS], _job_crests, list(CRESTS))
