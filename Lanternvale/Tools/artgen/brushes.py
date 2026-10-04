"""Painting toolkit for the Lanternvale placeholder art generator.

Only numpy + Pillow. Everything is deterministic: random generators are seeded from strings.

Concepts
--------
* A :class:`Canvas` holds a premultiplied float RGBA image. Painters address it in *design units*
  (``scale`` maps design units to pixels, so the same painter can render a sprite at 1x and a
  portrait crop at 2x). Optional padding plus ``wrap_x``/``wrap_y`` support seamless tiles.
* Masks are full-canvas float32 arrays (0..1). Heavy operations crop to the mask bounding box.
* Shapes are smooth closed Catmull-Rom splines or tapered strokes, rasterised with 4x supersampling.
* "Painterly" look = soft mask-normal shading (light from the upper left), cool violet shadows,
  warm cream lights, inner line work slightly darker than the fill, noise-modulated edges,
  watercolour pooling at wash edges, paper grain, and a soft dark outline around sprites.
"""
import hashlib
import math

import numpy as np
from PIL import Image, ImageDraw

import palette as P

F32 = np.float32


# ----------------------------------------------------------------------------------------
# determinism
# ----------------------------------------------------------------------------------------
def seed_of(*parts):
    h = hashlib.md5("/".join(str(p) for p in parts).encode("utf-8")).digest()
    return int.from_bytes(h[:8], "little")


def rng(*parts):
    return np.random.default_rng(seed_of(*parts))


# ----------------------------------------------------------------------------------------
# small math helpers
# ----------------------------------------------------------------------------------------
def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, dtype=F32) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def colfield(c0, c1, t):
    """Blend two colours by a (h,w) weight field -> (h,w,3)."""
    c0 = np.asarray(c0, F32)
    c1 = np.asarray(c1, F32)
    t = np.asarray(t, F32)[..., None]
    return c0 + (c1 - c0) * t


# ----------------------------------------------------------------------------------------
# blur (separable gaussian approximated by box passes, with wrap support)
# ----------------------------------------------------------------------------------------
def _box1d(a, r, axis, mode):
    if r <= 0:
        return a
    n = a.shape[axis]
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r + 1, r)
    if mode == "wrap":
        p = np.pad(a, pad, mode="wrap")
    elif mode == "edge":
        p = np.pad(a, pad, mode="edge")
    else:
        p = np.pad(a, pad)
    c = np.cumsum(p, axis=axis, dtype=np.float64)
    hi = [slice(None)] * a.ndim
    lo = [slice(None)] * a.ndim
    hi[axis] = slice(2 * r + 1, 2 * r + 1 + n)
    lo[axis] = slice(0, n)
    return ((c[tuple(hi)] - c[tuple(lo)]) / (2 * r + 1)).astype(F32)


def _box_sizes(sigma, n=3):
    w_ideal = math.sqrt(12.0 * sigma * sigma / n + 1.0)
    wl = int(math.floor(w_ideal))
    if wl % 2 == 0:
        wl -= 1
    wl = max(1, wl)
    wu = wl + 2
    m_ideal = (12.0 * sigma * sigma - n * wl * wl - 4.0 * n * wl - 3.0 * n) / (-4.0 * wl - 4.0)
    m = int(round(m_ideal))
    return [wl if i < m else wu for i in range(n)]


def _gauss_small(a, sigma, mx, my):
    r = max(1, int(math.ceil(sigma * 3)))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2).astype(F32)
    k /= k.sum()
    out = a
    for axis, mode in ((1, mx), (0, my)):
        n = out.shape[axis]
        pad = [(0, 0)] * out.ndim
        pad[axis] = (r, r)
        p = np.pad(out, pad, mode="wrap" if mode == "wrap" else ("edge" if mode == "edge" else "constant"))
        acc = np.zeros_like(out)
        for i in range(2 * r + 1):
            sl = [slice(None)] * out.ndim
            sl[axis] = slice(i, i + n)
            acc += k[i] * p[tuple(sl)]
        out = acc
    return out


def blur(a, sigma, wrap_x=False, wrap_y=False, mode="constant"):
    """Gaussian-ish blur of a 2D (h,w) or 3D (h,w,c) float array."""
    a = np.asarray(a, dtype=F32)
    if sigma <= 0.3:
        return a
    mx = "wrap" if wrap_x else mode
    my = "wrap" if wrap_y else mode
    if sigma < 1.8:
        return _gauss_small(a, sigma, mx, my)
    for s in _box_sizes(sigma, 3):
        r = (s - 1) // 2
        a = _box1d(a, r, 1, mx)
        a = _box1d(a, r, 0, my)
    return a


# ----------------------------------------------------------------------------------------
# noise
# ----------------------------------------------------------------------------------------
def _largest_divisor(n, target):
    target = max(1, min(n, int(round(target))))
    for d in range(target, 0, -1):
        if n % d == 0:
            return d
    return 1


def value_noise(h, w, cell, r, wrap_x=False, wrap_y=False):
    """Smooth value noise in 0..1 (bicubic upsampled random grid). Tileable on request.

    cell: scalar or (cell_y, cell_x) for anisotropic (streaky) noise.
    """
    if isinstance(cell, (tuple, list)):
        celly, cellx = max(1.0, float(cell[0])), max(1.0, float(cell[1]))
    else:
        celly = cellx = max(1.0, float(cell))
    pad = 3

    def axis_plan(size, wrap, c):
        if wrap:
            n = _largest_divisor(size, size / c)
            cs = size // n
            return n, n + 2 * pad, cs, pad * cs
        cs = max(1, int(round(c)))
        n = size // cs + 2
        return n, n, cs, 0

    nx, gx, csx, offx = axis_plan(w, wrap_x, cellx)
    ny, gy, csy, offy = axis_plan(h, wrap_y, celly)
    base = r.random((ny, nx)).astype(F32)
    g = base
    if wrap_x:
        g = np.pad(g, ((0, 0), (pad, pad)), mode="wrap")
    if wrap_y:
        g = np.pad(g, ((pad, pad), (0, 0)), mode="wrap")
    im = Image.fromarray(g, "F").resize((gx * csx, gy * csy), Image.BICUBIC)
    out = np.asarray(im, dtype=F32)[offy:offy + h, offx:offx + w]
    return out


def fbm(h, w, cell, r, octaves=4, gain=0.5, wrap_x=False, wrap_y=False):
    """Fractal value noise, approx mean 0.5, std ~0.2, roughly 0..1. cell scalar or (cy, cx)."""
    total = np.zeros((h, w), F32)
    amp = 1.0
    norm2 = 0.0
    if isinstance(cell, (tuple, list)):
        cy, cx = float(cell[0]), float(cell[1])
    else:
        cy = cx = float(cell)
    for _ in range(octaves):
        if max(cx, cy) < 1.0:
            break
        total += amp * (value_noise(h, w, (max(1.0, cy), max(1.0, cx)), r, wrap_x, wrap_y) - 0.5)
        norm2 += amp * amp
        amp *= gain
        cx /= 2.0
        cy /= 2.0
    return 0.5 + total / math.sqrt(max(norm2, 1e-6))


def periodic_1d(n, r, harmonics, period=None):
    """Smooth periodic 1D curve with integer frequencies (seamless horizontally).

    harmonics: list of (frequency, amplitude). Returns array of length n, roughly -sum(amp)..+sum(amp).
    """
    x = np.arange(n, dtype=F32) / float(period or n)
    out = np.zeros(n, F32)
    for f, amp in harmonics:
        ph = r.random() * 2 * math.pi
        out += amp * np.sin(2 * math.pi * f * x + ph)
    return out


def periodic_noise_1d(n, r, cell, octaves=4, gain=0.5, period=None):
    """Periodic 1D fbm (value noise) of length n with period `period` (default n)."""
    period = period or n
    row = fbm(1, period, cell, r, octaves=octaves, gain=gain, wrap_x=True)[0]
    idx = np.arange(n) % period
    return row[idx]


# ----------------------------------------------------------------------------------------
# geometry
# ----------------------------------------------------------------------------------------
def catmull(pts, closed=True, n=10):
    """Uniform Catmull-Rom spline through pts -> dense polyline (N,2)."""
    p = np.asarray(pts, dtype=np.float64)
    if len(p) < 3:
        return p
    if closed:
        pp = np.concatenate([p[-1:], p, p[:2]])
        segs = len(p)
    else:
        pp = np.concatenate([p[:1] * 2 - p[1:2], p, p[-1:] * 2 - p[-2:-1]])
        segs = len(p) - 1
    t = np.linspace(0, 1, n, endpoint=False)[:, None]
    t2, t3 = t * t, t * t * t
    out = []
    for i in range(segs):
        p0, p1, p2, p3 = pp[i], pp[i + 1], pp[i + 2], pp[i + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    if not closed:
        out.append(p[-1:])
    return np.concatenate(out)


def ellipse(cx, cy, rx, ry, rot=0.0, n=72, a0=0.0, a1=2 * math.pi):
    t = np.linspace(a0, a1, n, endpoint=(a1 - a0) < 2 * math.pi - 1e-6)
    x = rx * np.cos(t)
    y = ry * np.sin(t)
    c, s = math.cos(rot), math.sin(rot)
    return np.stack([cx + x * c - y * s, cy + x * s + y * c], 1)


def rotate(pts, ang, cx=0.0, cy=0.0):
    p = np.asarray(pts, dtype=np.float64) - (cx, cy)
    c, s = math.cos(ang), math.sin(ang)
    return np.stack([p[:, 0] * c - p[:, 1] * s + cx, p[:, 0] * s + p[:, 1] * c + cy], 1)


def blob(cx, cy, rx, ry, r, k=0.18, n=9, rot=0.0):
    """Organic closed blob: an ellipse with jittered radii, smoothed."""
    ang = np.linspace(0, 2 * math.pi, n, endpoint=False) + r.random() * 6.28
    rad = 1.0 + (r.random(n) - 0.5) * 2 * k
    pts = np.stack([cx + np.cos(ang) * rx * rad, cy + np.sin(ang) * ry * rad], 1)
    if rot:
        pts = rotate(pts, rot, cx, cy)
    return catmull(pts, True, 10)


def stroke(pts, w0, w1=None, n=10, profile=None, cap=True, smooth=True):
    """Tapered stroke polygon along a centreline. Widths are full widths.

    profile(t) -> width multiplier overrides the linear w0->w1 taper.
    """
    if w1 is None:
        w1 = w0
    c = catmull(pts, closed=False, n=n) if (smooth and len(pts) >= 3) else np.asarray(pts, np.float64)
    if len(c) < 2:
        return c
    seg = np.diff(c, axis=0)
    L = np.concatenate([[0], np.cumsum(np.hypot(seg[:, 0], seg[:, 1]))])
    t = L / max(L[-1], 1e-6)
    tang = np.gradient(c, axis=0)
    tang /= np.maximum(np.hypot(tang[:, 0], tang[:, 1]), 1e-9)[:, None]
    nrm = np.stack([-tang[:, 1], tang[:, 0]], 1)
    if profile is not None:
        w = np.array([profile(v) for v in t]) * w0
    else:
        w = w0 + (w1 - w0) * t
    left = c + nrm * (w / 2)[:, None]
    right = c - nrm * (w / 2)[:, None]
    poly = [left]
    if cap and w[-1] > 0.5:
        a = math.atan2(tang[-1, 1], tang[-1, 0])
        poly.append(ellipse(c[-1, 0], c[-1, 1], w[-1] / 2, w[-1] / 2, 0, 9, a + math.pi / 2, a - math.pi / 2)[1:-1])
    poly.append(right[::-1])
    if cap and w[0] > 0.5:
        a = math.atan2(tang[0, 1], tang[0, 0])
        poly.append(ellipse(c[0, 0], c[0, 1], w[0] / 2, w[0] / 2, 0, 9, a - math.pi / 2, a - 3 * math.pi / 2)[1:-1])
    return np.concatenate(poly)


def taper(t, start=0.25, end=1.0, power=1.0):
    """Profile helper: width rises quickly then tapers to a point."""
    up = min(1.0, t / max(start, 1e-6)) if start > 0 else 1.0
    down = (1.0 - t) ** power if end else 1.0
    return max(0.0, math.sqrt(up) * down)


def lerp_pts(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


# ----------------------------------------------------------------------------------------
# canvas
# ----------------------------------------------------------------------------------------
class Canvas:
    """Premultiplied float RGBA canvas addressed in design units."""

    def __init__(self, w, h, scale=1.0, pad=0, wrap_x=False, wrap_y=False, seed="canvas"):
        self.w, self.h, self.s = w, h, float(scale)
        self.cw = int(round(w * scale))
        self.ch = int(round(h * scale))
        self.padx = pad if wrap_x else 0
        self.pady = pad if wrap_y else 0
        self.PW = self.cw + 2 * self.padx
        self.PH = self.ch + 2 * self.pady
        self.wrap_x, self.wrap_y = wrap_x, wrap_y
        self.rgb = np.zeros((self.PH, self.PW, 3), F32)
        self.a = np.zeros((self.PH, self.PW), F32)
        self.seed = seed
        self._noise_cache = {}

    # --- coordinates -----------------------------------------------------------------
    def T(self, pts):
        p = np.asarray(pts, dtype=np.float64)
        return np.stack([p[:, 0] * self.s + self.padx, p[:, 1] * self.s + self.pady], 1)

    def px(self, v):
        return v * self.s

    def yy(self):
        """Design-unit y coordinate of every pixel row (column vector)."""
        return ((np.arange(self.PH, dtype=F32) - self.pady + 0.5) / self.s)[:, None]

    def xx(self):
        return ((np.arange(self.PW, dtype=F32) - self.padx + 0.5) / self.s)[None, :]

    # --- noise in canvas pixel space (tileable when the canvas wraps) -------------------
    def noise(self, cell, key, octaves=4, gain=0.5):
        """fbm sized to the canvas; `cell` in design units. Periodic over the unpadded size."""
        ck = (tuple(cell) if isinstance(cell, (tuple, list)) else round(cell, 3), key, octaves, gain)
        if ck in self._noise_cache:
            return self._noise_cache[ck]
        r = rng(self.seed, "noise", key)
        h = self.ch if self.wrap_y else self.PH
        w = self.cw if self.wrap_x else self.PW
        c = (cell[0] * self.s, cell[1] * self.s) if isinstance(cell, (tuple, list)) else cell * self.s
        n = fbm(h, w, c, r, octaves, gain, self.wrap_x, self.wrap_y)
        if self.wrap_x and self.padx:
            n = n[:, (np.arange(self.PW) - self.padx) % self.cw]
        if self.wrap_y and self.pady:
            n = n[(np.arange(self.PH) - self.pady) % self.ch, :]
        n = np.ascontiguousarray(n, dtype=F32)
        self._noise_cache[ck] = n
        return n

    def blur(self, a, sigma_design):
        # padded wrap canvases blur with zero padding: the pad absorbs edge effects
        pw = self.wrap_x and not self.padx
        ph = self.wrap_y and not self.pady
        return blur(a, sigma_design * self.s, pw, ph)

    def profile_mask(self, prof_design, soft=1.0):
        """Mask of everything below a per-column profile (design-unit y per canvas column)."""
        Y = (np.arange(self.PH, dtype=F32)[:, None] - self.pady + 0.5) / self.s
        return np.clip((Y - np.asarray(prof_design, F32)[None, :]) * self.s / soft + 0.5, 0, 1)

    def periodic(self, r, harmonics):
        """Per-column periodic curve (period = unpadded width), sampled for every padded column."""
        base = periodic_1d(self.cw, r, harmonics)
        return base[(np.arange(self.PW) - self.padx) % self.cw]

    def periodic_noise(self, r, cell, octaves=4, gain=0.5):
        base = periodic_noise_1d(self.cw, r, cell * self.s, octaves, gain)
        return base[(np.arange(self.PW) - self.padx) % self.cw]

    def sub(self, x0, y0, x1, y1):
        """A view onto a rectangle (design units) of this canvas. Painting into it paints the parent.
        Returns None if the rectangle is off-canvas. Noise is sliced from the parent (coherent)."""
        px0 = max(0, int(math.floor(x0 * self.s + self.padx)))
        py0 = max(0, int(math.floor(y0 * self.s + self.pady)))
        px1 = min(self.PW, int(math.ceil(x1 * self.s + self.padx)))
        py1 = min(self.PH, int(math.ceil(y1 * self.s + self.pady)))
        if px1 - px0 < 2 or py1 - py0 < 2:
            return None
        c = SubCanvas.__new__(SubCanvas)
        c.w, c.h, c.s = self.w, self.h, self.s
        c.cw, c.ch = self.cw, self.ch
        c.padx = self.padx - px0
        c.pady = self.pady - py0
        c.PW = px1 - px0
        c.PH = py1 - py0
        c.wrap_x = c.wrap_y = False
        c.rgb = self.rgb[py0:py1, px0:px1]
        c.a = self.a[py0:py1, px0:px1]
        c.seed = self.seed
        c.root = getattr(self, "root", self)
        c.sl = (slice(py0 + getattr(self, "oy", 0), py1 + getattr(self, "oy", 0)),
                slice(px0 + getattr(self, "ox", 0), px1 + getattr(self, "ox", 0)))
        c.oy = py0 + getattr(self, "oy", 0)
        c.ox = px0 + getattr(self, "ox", 0)
        c._noise_cache = {}
        return c

    def copies_x(self):
        return (-self.w, 0.0, self.w) if self.wrap_x else (0.0,)

    def copies_y(self):
        return (-self.h, 0.0, self.h) if self.wrap_y else (0.0,)

    # --- masks -----------------------------------------------------------------------
    def polys_mask(self, polys, ss=4, value_ops=None):
        """Rasterise one or more polygons (design units) into an anti-aliased full mask.

        value_ops: optional list of fill values (255 add, 0 cut) per polygon, drawn in order.
        """
        polys = [self.T(p) for p in polys if p is not None and len(p) >= 3]
        m = np.zeros((self.PH, self.PW), F32)
        if not polys:
            return m
        allp = np.concatenate(polys)
        x0 = int(max(0, math.floor(allp[:, 0].min()) - 2))
        y0 = int(max(0, math.floor(allp[:, 1].min()) - 2))
        x1 = int(min(self.PW, math.ceil(allp[:, 0].max()) + 2))
        y1 = int(min(self.PH, math.ceil(allp[:, 1].max()) + 2))
        if x1 <= x0 or y1 <= y0:
            return m
        img = Image.new("L", ((x1 - x0) * ss, (y1 - y0) * ss), 0)
        d = ImageDraw.Draw(img)
        for i, p in enumerate(polys):
            v = 255 if value_ops is None else value_ops[i]
            d.polygon([((x - x0) * ss, (y - y0) * ss) for x, y in p], fill=v)
        small = img.resize((x1 - x0, y1 - y0), Image.BOX)
        m[y0:y1, x0:x1] = np.asarray(small, dtype=F32) / 255.0
        return m

    def mask(self, *polys):
        return self.polys_mask(list(polys))

    def ellipse_mask(self, cx, cy, rx, ry, rot=0.0):
        return self.polys_mask([ellipse(cx, cy, rx, ry, rot, n=max(48, int(self.s * (rx + ry) * 0.6)))])

    def lines_mask(self, segs, width, ss=4):
        """Many thin straight segments [(x0,y0,x1,y1)] (design units) into one mask."""
        m = np.zeros((self.PH, self.PW), F32)
        if not len(segs):
            return m
        img = Image.new("L", (self.PW * ss, self.PH * ss), 0) if self.PW * self.PH * ss * ss < 4e7 else None
        if img is None:
            ss = 2
            img = Image.new("L", (self.PW * ss, self.PH * ss), 0)
        d = ImageDraw.Draw(img)
        wpx = max(1, int(round(width * self.s * ss)))
        for x0, y0, x1, y1 in segs:
            a = self.T([(x0, y0), (x1, y1)]) * ss
            d.line([tuple(a[0]), tuple(a[1])], fill=255, width=wpx)
        small = img.resize((self.PW, self.PH), Image.BOX)
        return np.asarray(small, dtype=F32) / 255.0

    # --- compositing -----------------------------------------------------------------
    def over(self, color, alpha, region=None):
        """Composite colour (3,) or (h,w,3) with alpha (h,w) over the canvas."""
        if region is None:
            ys, xs = slice(None), slice(None)
        else:
            ys, xs = region
        al = np.clip(alpha, 0, 1)[..., None]
        col = np.asarray(color, F32)
        rgb = self.rgb[ys, xs]
        self.rgb[ys, xs] = col * al + rgb * (1.0 - al)
        self.a[ys, xs] = al[..., 0] + self.a[ys, xs] * (1.0 - al[..., 0])

    def under(self, color, alpha, region=None):
        if region is None:
            ys, xs = slice(None), slice(None)
        else:
            ys, xs = region
        al = np.clip(alpha, 0, 1)
        col = np.asarray(color, F32)
        da = self.a[ys, xs]
        k = (al * (1.0 - da))[..., None]
        self.rgb[ys, xs] = self.rgb[ys, xs] + col * k
        self.a[ys, xs] = da + k[..., 0]

    def atop(self, color, t, region=None):
        """Mix existing paint toward colour by weight t, clipped to existing alpha."""
        if region is None:
            ys, xs = slice(None), slice(None)
        else:
            ys, xs = region
        a = self.a[ys, xs][..., None]
        col = np.asarray(color, F32)
        tt = np.clip(t, 0, 1)[..., None]
        rgb = self.rgb[ys, xs]
        self.rgb[ys, xs] = rgb + (col * a - rgb) * tt

    def erase(self, mask, region=None):
        if region is None:
            ys, xs = slice(None), slice(None)
        else:
            ys, xs = region
        k = 1.0 - np.clip(mask, 0, 1)
        self.rgb[ys, xs] *= k[..., None]
        self.a[ys, xs] *= k

    def multiply(self, factor):
        """Multiply colour (keeps alpha). factor (h,w) or (h,w,3)."""
        f = np.asarray(factor, F32)
        if f.ndim == 2:
            f = f[..., None]
        self.rgb *= f

    # --- final conversion --------------------------------------------------------------
    def straight(self):
        a = self.a
        rgb = self.rgb / np.maximum(a, 1e-6)[..., None]
        return np.clip(rgb, 0, 1), np.clip(a, 0, 1)

    def crop_pad(self):
        ys = slice(self.pady, self.pady + self.ch)
        xs = slice(self.padx, self.padx + self.cw)
        return self.rgb[ys, xs], self.a[ys, xs]


class SubCanvas(Canvas):
    """View onto part of a canvas (see Canvas.sub)."""

    def noise(self, cell, key, octaves=4, gain=0.5):
        return self.root.noise(cell, key, octaves, gain)[self.sl]


def bbox(mask, pad=0, thresh=1e-3):
    """Slices of the nonzero area of a mask (with padding), or None."""
    rows = np.where(mask.max(axis=1) > thresh)[0]
    if not len(rows):
        return None
    cols = np.where(mask.max(axis=0) > thresh)[0]
    h, w = mask.shape
    y0 = max(0, rows[0] - pad)
    y1 = min(h, rows[-1] + 1 + pad)
    x0 = max(0, cols[0] - pad)
    x1 = min(w, cols[-1] + 1 + pad)
    return slice(y0, y1), slice(x0, x1)


# ----------------------------------------------------------------------------------------
# painters
# ----------------------------------------------------------------------------------------
def warp(cv, mask, amp, cell, key):
    """Domain-warp a mask by noise (organic, hand-cut edges). amp/cell in design units."""
    if amp <= 0:
        return mask
    pad = int(amp * cv.s) + 3
    bb = bbox(mask, pad)
    if bb is None:
        return mask
    ys, xs = bb
    nx = cv.noise(cell, key + "wx", octaves=2)[ys, xs]
    ny = cv.noise(cell, key + "wy", octaves=2)[ys, xs]
    h, w = nx.shape
    gy, gx = np.mgrid[0:h, 0:w].astype(F32)
    sx = np.clip(gx + (nx - 0.5) * 2 * amp * cv.s, 0, w - 1.001)
    sy = np.clip(gy + (ny - 0.5) * 2 * amp * cv.s, 0, h - 1.001)
    sub = mask[ys, xs]
    x0 = sx.astype(np.int32)
    y0 = sy.astype(np.int32)
    fx = sx - x0
    fy = sy - y0
    v = (sub[y0, x0] * (1 - fx) * (1 - fy) + sub[y0, x0 + 1] * fx * (1 - fy)
         + sub[y0 + 1, x0] * (1 - fx) * fy + sub[y0 + 1, x0 + 1] * fx * fy)
    out = mask.copy()
    out[ys, xs] = v
    return out


def ragged(cv, mask, amount=0.25, soft=1.2, cell=6, key="rag"):
    """Noise-thresholded edge: rough watercolour / brush edge. soft & cell in design units."""
    bb = bbox(mask, int(soft * cv.s * 3) + 2)
    if bb is None:
        return mask
    ys, xs = bb
    b = blur(mask[ys, xs], soft * cv.s)
    n = cv.noise(cell, key, octaves=3)[ys, xs]
    v = b + (n - 0.5) * amount
    out = mask.copy()
    out[ys, xs] = smoothstep(0.42, 0.58, v)
    return out


def dilate(cv, mask, r_design, soft=0.6):
    """Approximate dilation by blur + threshold (r in design units)."""
    r = r_design * cv.s
    if r <= 0.2:
        return mask
    sig = max(0.6, r / 1.6)
    bb = bbox(mask, int(r * 3) + 3)
    if bb is None:
        return mask
    ys, xs = bb
    b = blur(mask[ys, xs], sig, cv.wrap_x and False, False)
    out = mask.copy()
    out[ys, xs] = np.maximum(mask[ys, xs], smoothstep(0.03, 0.03 + 0.12 * soft + 0.02, b))
    return out


def erode(cv, mask, r_design):
    inv = 1.0 - mask
    return 1.0 - dilate(cv, inv, r_design)


def paint(cv, mask, base, *, shade=1.0, hi=0.35, soft=None, light=None, line=0.85, line_w=1.4,
          ao=0.25, var=0.06, var_cell=18, key="p", lo=None, light_col=None, flat=False,
          grad=None, tex=None, rim=0.0, opacity=1.0, line_col=None, gloss=0.0, cel=0.5, nmask=None):
    """Paint a solid part with soft volumetric shading and inner line work.

    base: colour, shade: shadow strength, hi: highlight strength, soft: normal blur (design units),
    ao: darken toward the bottom of the part, var: low-frequency colour variation,
    grad: (top_colour, bottom_colour) optional vertical gradient replacing base,
    tex: optional (h,w) texture field (0..1, 0.5 neutral) multiplied into value,
    cel: 0 = smooth shading, 1 = hard two-tone anime shading.
    nmask: optional mask used for normals/line work (alpha still comes from mask).
    """
    pad = int(max(4, (soft or 8) * cv.s * 3))
    bb = bbox(mask if nmask is None else np.maximum(mask, nmask), pad)
    if bb is None:
        return
    ys, xs = bb
    m = mask[ys, xs]
    mn = m if nmask is None else nmask[ys, xs]
    h, w = m.shape
    area = float(m.sum()) / (cv.s * cv.s)
    size = math.sqrt(max(area, 1.0))
    sig_d = soft if soft is not None else float(np.clip(size * 0.13, 1.6, 26.0))
    sig = sig_d * cv.s
    L = None
    base = np.asarray(base, F32)
    if grad is not None:
        rows = np.where(m.max(1) > 0.01)[0]
        r0, r1 = (rows[0], rows[-1]) if len(rows) else (0, h - 1)
        t = np.clip((np.arange(h) - r0) / max(1, r1 - r0), 0, 1)[:, None]
        col = colfield(grad[0], grad[1], np.broadcast_to(t, (h, w)))
    else:
        col = np.broadcast_to(base, (h, w, 3)).astype(F32).copy()
    if not flat:
        bm = blur(mn, sig)
        gy, gx = np.gradient(bm)
        k = sig * 2.6
        nx, ny = -gx * k, -gy * k
        nz = np.ones_like(nx)
        inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
        ld = P.LIGHT_DIR if light is None else np.asarray(light, F32) / np.linalg.norm(light)
        L = (nx * ld[0] + ny * ld[1] + nz * ld[2]) * inv
        lo_c = np.asarray(lo if lo is not None else P.shadow_of(tuple(base), 1.0), F32)
        li_c = np.asarray(light_col if light_col is not None else P.light_of(tuple(base), 1.0), F32)
        e0 = 0.42 + 0.06 * cel
        e1 = e0 + 0.30 * (1.0 - cel) + 0.04
        tsh = 1.0 - smoothstep(e0, e1, L)
        if grad is not None:
            g1 = np.asarray(grad[1], F32)
            shadow_field = col + (np.asarray(P.shadow_of(tuple(g1), 1.0), F32) - g1)
        else:
            shadow_field = np.broadcast_to(lo_c, (h, w, 3))
        col = col + (shadow_field - col) * (tsh * shade)[..., None]
        thi = smoothstep(0.80, 0.96, L) * hi
        col = col + (li_c - col) * thi[..., None]
    if ao > 0:
        rows = np.where(m.max(1) > 0.01)[0]
        if len(rows):
            r0, r1 = rows[0], rows[-1]
            t = np.clip((np.arange(h) - r0) / max(1, r1 - r0), 0, 1)[:, None]
            col = col * (1.0 - ao * smoothstep(0.55, 1.0, t) * 0.5)[..., None]
    if var > 0:
        n = cv.noise(var_cell, key + "var", octaves=3)[ys, xs]
        col = col * (1.0 + (n - 0.5) * 2 * var)[..., None]
        warm = np.asarray(P.CREAM_WARM, F32)
        col = col + (warm - col) * (np.clip(n - 0.6, 0, 1) * var * 1.5)[..., None]
    if tex is not None:
        tx = tex[ys, xs] if tex.shape == mask.shape else tex
        col = col * (0.75 + 0.5 * tx)[..., None]
    if gloss > 0 and L is not None:
        g = smoothstep(0.9, 0.99, L) * gloss
        col = col + (np.asarray(P.WHITE_WARM, F32) - col) * g[..., None]
    if line > 0 and line_w > 0:
        lw = line_w * cv.s
        inner = smoothstep(0.55, 0.98, blur(mn, max(0.7, lw * 0.75)))
        edge = np.clip(mn - inner, 0, 1) * line
        lc = np.asarray(line_col if line_col is not None else P.line_of(tuple(base)), F32)
        col = col + (lc - col) * np.clip(edge * 1.6, 0, 1)[..., None]
    cv.over(np.clip(col, 0, 1.2), m * opacity, (ys, xs))
    return L


def flat_fill(cv, mask, color, opacity=1.0):
    bb = bbox(mask, 1)
    if bb is None:
        return
    ys, xs = bb
    cv.over(np.asarray(color, F32), mask[ys, xs] * opacity, (ys, xs))


def drop_shadow(cv, mask, dx=3, dy=5, soft=4, strength=0.35, color=None):
    """Darken already painted pixels under a (shifted, blurred) mask: contact shadow between layers."""
    pad = int((abs(dx) + abs(dy) + soft * 3) * cv.s) + 3
    bb = bbox(mask, pad)
    if bb is None:
        return
    ys, xs = bb
    sub = mask[ys, xs]
    sh = np.roll(np.roll(sub, int(round(dy * cv.s)), 0), int(round(dx * cv.s)), 1)
    sh = blur(sh, soft * cv.s) * (1.0 - sub)
    col = np.asarray(color if color is not None else P.mix(P.INK, P.VIOLET, 0.3), F32)
    cv.atop(col, sh * strength, (ys, xs))


def wash(cv, mask, color, opacity=0.9, color2=None, var=0.10, var_cell=60, mix_cell=140, pool=0.35,
         pool_w=3.0, gran=0.10, gran_cell=2.5, key="w", grad=None, bloom=0.0, field=None):
    """Watercolour wash: translucent, colour-varied, pigment pooling at edges, granulation.

    grad: optional full-size (H,W) 0..1 field blending color -> color2 (instead of noise mixing).
    field: optional full-size (H,W,3) colour field replacing color/color2.
    """
    pad = int(pool_w * cv.s * 3) + 2
    bb = bbox(mask, pad)
    if bb is None:
        return
    ys, xs = bb
    m = mask[ys, xs]
    h, w = m.shape
    c1 = np.asarray(color, F32)
    if field is not None:
        col = np.array(field[ys, xs], dtype=F32)
    elif color2 is not None:
        if grad is not None:
            t = grad[ys, xs]
        else:
            t = smoothstep(0.3, 0.7, cv.noise(mix_cell, key + "mix", octaves=3)[ys, xs])
        col = colfield(c1, color2, t)
    else:
        col = np.broadcast_to(c1, (h, w, 3)).astype(F32).copy()
    if var > 0:
        n = cv.noise(var_cell, key + "var", octaves=4)[ys, xs]
        col = col * (1.0 + (n - 0.5) * 2 * var)[..., None]
    if pool > 0:
        inner = blur(m, pool_w * cv.s)
        edge = np.clip(m - inner, 0, 1) * 2.0
        dk = col * 0.78 + np.asarray(P.VIOLET, F32) * 0.08
        col = col + (dk - col) * np.clip(edge * pool * 2.0, 0, 1)[..., None]
    if bloom > 0:
        bn = cv.noise(var_cell * 0.6, key + "bloom", octaves=2)[ys, xs]
        b = smoothstep(0.62, 0.75, bn) * bloom
        col = col + (np.asarray(P.CREAM, F32) - col) * (b * 0.5)[..., None]
    al = m * opacity
    if gran > 0:
        g = cv.noise(gran_cell, key + "gran", octaves=2)[ys, xs]
        al = al * (1.0 - gran * (g - 0.35))
        col = col * (1.0 - gran * 0.5 * (g - 0.5))[..., None]
    cv.over(np.clip(col, 0, 1), np.clip(al, 0, 1), (ys, xs))


def glow(cv, cx, cy, r, color, strength=1.0, clip=True, falloff=2.0):
    """Soft radial light painted onto existing paint (clipped to alpha) or over it."""
    R = r * cv.s
    c = cv.T([(cx, cy)])[0]
    x0 = int(max(0, c[0] - R * 1.2))
    x1 = int(min(cv.PW, c[0] + R * 1.2 + 1))
    y0 = int(max(0, c[1] - R * 1.2))
    y1 = int(min(cv.PH, c[1] + R * 1.2 + 1))
    if x1 <= x0 or y1 <= y0:
        return
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(F32)
    d = np.sqrt((xx - c[0]) ** 2 + (yy - c[1]) ** 2) / max(R, 1e-3)
    g = np.clip(1.0 - d, 0, 1) ** falloff * strength
    reg = (slice(y0, y1), slice(x0, x1))
    if clip:
        cv.atop(color, g, reg)
    else:
        cv.over(color, g, reg)


def grain(cv, amount=0.04, cell=1.6, key="grain", fiber=0.5):
    """Paper grain: subtle multiplicative noise (fine speckle + horizontal fibres)."""
    n1 = cv.noise(cell, key + "g1", octaves=2)
    f = cv.noise(cell * 6, key + "g2", octaves=2)
    n = n1 * (1 - fiber * 0.5) + f * fiber * 0.5
    cv.multiply(1.0 + (n - 0.5) * 2 * amount)


def rim_light(cv, amount=0.35, width=3.0, color=None, direction=(1.0, -1.0)):
    """Lighten the silhouette edge facing the back light (upper right by default)."""
    a = cv.a
    d = width * cv.s
    dx = int(round(-direction[0] * d))
    dy = int(round(-direction[1] * d))
    sh = np.roll(np.roll(a, dy, 0), dx, 1)
    rim = np.clip(a - sh, 0, 1)
    rim = blur(rim, max(0.7, d * 0.35)) * a
    col = np.asarray(color if color is not None else P.CREAM, F32)
    cv.atop(col, np.clip(rim * amount, 0, 1))


def outline(cv, width=2.6, darkness=1.0, ink=None, soft=0.8):
    """Soft dark outline around the whole sprite, coloured slightly darker than the local fill."""
    a = cv.a
    if a.max() <= 0:
        return
    W = width * cv.s
    bb = bbox(a, int(W * 3) + 4)
    ys, xs = bb
    sub = a[ys, xs]
    sig = max(0.6, W / 1.7)
    b = blur(sub, sig)
    dil = smoothstep(0.035, 0.035 + 0.10 * soft + 0.03, b)
    dil = np.maximum(dil, sub)
    # local fill colour
    cb = blur(cv.rgb[ys, xs], max(1.5, W))
    ab = blur(sub, max(1.5, W))
    loc = cb / np.maximum(ab, 1e-4)[..., None]
    ink_c = np.asarray(ink if ink is not None else P.INK, F32)
    lc = loc * 0.38 + ink_c * 0.62
    lc = lc * (1.0 - 0.15 * darkness)
    cv.under(np.clip(lc, 0, 1), dil, (ys, xs))


def grade(rgb, warm=0.04, lift=0.03):
    """Final unifying colour grade: violet-tinted shadows, warm cream highlights."""
    lum = rgb[..., 0] * 0.3 + rgb[..., 1] * 0.55 + rgb[..., 2] * 0.15
    sh = (1.0 - lum)[..., None] ** 2
    hi = lum[..., None] ** 2
    out = rgb + (np.asarray(P.VIOLET, F32) - rgb) * sh * lift
    out = out + (np.asarray(P.CREAM_WARM, F32) - out) * hi * warm
    return np.clip(out, 0, 1)


def downsample(rgb, a, factor):
    if factor == 1:
        return rgb, a
    h, w = a.shape
    h2, w2 = h // factor, w // factor
    rgb = rgb[:h2 * factor, :w2 * factor].reshape(h2, factor, w2, factor, 3).mean(axis=(1, 3))
    a = a[:h2 * factor, :w2 * factor].reshape(h2, factor, w2, factor).mean(axis=(1, 3))
    return rgb.astype(F32), a.astype(F32)


def to_image(rgb_pm, a, bg_rgb=None, do_grade=True, bleed=False, qstep=2):
    """Premultiplied float -> straight RGBA uint8 PIL image.

    Fully transparent pixels get a constant colour (bg_rgb, default ink) so PNGs compress well and
    bilinear filtering does not create bright fringes. RGB is quantised to multiples of `qstep`
    (visually lossless, ~15 % smaller PNGs); alpha keeps full precision.
    """
    a = np.clip(a, 0, 1)
    rgb = rgb_pm / np.maximum(a, 1e-6)[..., None]
    rgb = np.clip(rgb, 0, 1)
    if do_grade:
        rgb = grade(rgb)
    if bleed:
        cb = blur(rgb_pm, 6.0, mode="edge")
        ab = blur(a, 6.0, mode="edge")
        fill = np.clip(cb / np.maximum(ab, 1e-4)[..., None], 0, 1)
        far = (ab < 1e-3)[..., None]
        fill = np.where(far, np.asarray(bg_rgb if bg_rgb is not None else P.INK, F32), fill)
        rgb = np.where((a <= 1.0 / 255)[..., None], fill, rgb)
    else:
        bgc = np.asarray(bg_rgb if bg_rgb is not None else P.INK, F32)
        rgb = np.where((a <= 1.0 / 255)[..., None], bgc, rgb)
    a8 = np.round(a * 255).astype(np.uint8)
    if qstep and qstep > 1:
        rgb8 = np.minimum(255, np.round(rgb * 255 / qstep) * qstep).astype(np.uint8)
    else:
        rgb8 = np.round(rgb * 255).astype(np.uint8)
    out = np.dstack([rgb8, a8])
    return Image.fromarray(out, "RGBA")
