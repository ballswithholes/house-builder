#!/usr/bin/env python3
"""Preview contact sheets for the generated art (reads Resources/Art + art_manifest.json).

    python3 Lanternvale/Tools/artgen/contact_sheet.py

Writes Docs/art_previews/*.png (each kept under 1.5 MB):
  env_composite.png      a diorama-like scene: sky, cloud/mountain/hill/village layers, ground, props,
                         party + creatures, foreground - placed with manifest heights and pivots
  env_layers.png         every background loop (tiled twice to show the seam) + ground tiles + decals
  props.png              all midground props
  characters.png         heroes and companions with their portraits
  npcs.png               villagers with portraits
  creatures.png          creatures, pets, demons, totems
  icons.png              every glyph on a school-coloured frame + class crests + effects
"""
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import numpy as np  # noqa: E402
from PIL import Image, ImageDraw, ImageFilter, ImageFont  # noqa: E402

import keys as K  # noqa: E402

MAX_BYTES = int(1.45 * 1024 * 1024)


def load_manifest():
    with open(os.path.join(K.ART_DIR, "art_manifest.json"), encoding="utf-8") as f:
        return {e["key"]: e for e in json.load(f)["entries"]}


MAN = None


def img(key):
    e = MAN[key]
    p = os.path.join(K.ROOT, "Assets", "Lanternvale", "Resources", e["path"] + ".png")
    return Image.open(p).convert("RGBA")


def font(size):
    for f in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def save(im, name):
    os.makedirs(K.PREVIEW_DIR, exist_ok=True)
    path = os.path.join(K.PREVIEW_DIR, name)
    im = im.convert("RGB")
    b = io.BytesIO()
    im.save(b, "PNG", optimize=True)
    if b.tell() > MAX_BYTES:
        q = im.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG)
        b = io.BytesIO()
        q.save(b, "PNG", optimize=True)
        scale = 0.9
        while b.tell() > MAX_BYTES and scale > 0.3:
            sm = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
            q = sm.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG)
            b = io.BytesIO()
            q.save(b, "PNG", optimize=True)
            scale *= 0.9
    with open(path, "wb") as f:
        f.write(b.getvalue())
    print("  %-22s %4d KB" % (name, len(b.getvalue()) // 1024))


def sky(w, h, top=(143, 182, 216), mid=(190, 212, 230), hor=(248, 230, 204)):
    t = np.linspace(0, 1, h)[:, None, None]
    top, mid, hor = np.array(top, float), np.array(mid, float), np.array(hor, float)
    c = np.where(t < 0.55, top + (mid - top) * (t / 0.55), mid + (hor - mid) * ((t - 0.55) / 0.45))
    a = np.broadcast_to(c, (h, w, 3)).astype(np.uint8)
    return Image.fromarray(np.dstack([a, np.full((h, w), 255, np.uint8)]), "RGBA")


def scaled(key, ppm, flip=False):
    """Sprite scaled so the image is manifest-height metres tall at `ppm` pixels per metre."""
    im = img(key)
    e = MAN[key]
    h = max(1, int(round(e["height"] * ppm)))
    w = max(1, int(round(im.width * h / im.height)))
    im = im.resize((w, h), Image.LANCZOS)
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    return im


def place(canvas, key, x, y, ppm, flip=False, shadow=True):
    """Place a sprite with its pivot at (x, y)."""
    e = MAN[key]
    im = scaled(key, ppm, flip)
    px = e["pivot"][0] if not flip else 1 - e["pivot"][0]
    ox = int(round(x - im.width * px))
    oy = int(round(y - im.height * (1 - e["pivot"][1])))
    if shadow and e.get("shadow"):
        sh = img("fx_shadow")
        sw = int(im.width * 0.55)
        sh = sh.resize((max(1, sw), max(1, sw // 3)), Image.LANCZOS)
        a = np.asarray(sh).copy()
        a[..., 3] = (a[..., 3] * 0.55).astype(np.uint8)
        sh = Image.fromarray(a, "RGBA")
        canvas.alpha_composite(sh, (int(x - sh.width / 2), int(y - sh.height / 2)))
    canvas.alpha_composite(im, (ox, oy))


def tile_layer(canvas, key, top_y, ppm, offset=0):
    """Tile a horizontal loop layer across the canvas with its top at top_y."""
    e = MAN[key]
    im = scaled(key, ppm)
    x = -int(offset) % im.width - im.width
    while x < canvas.width:
        canvas.alpha_composite(im, (x, int(top_y)))
        x += im.width


def env_composite():
    W, H = 1600, 900
    ppm = 66.0
    c = sky(W, H)
    tile_layer(c, "bg_clouds", -60, ppm * 1.0, 300)
    tile_layer(c, "bg_mountains_far", 20, ppm * 0.62, 120)
    tile_layer(c, "bg_hills_far", 150, ppm * 0.62, 500)
    tile_layer(c, "bg_village_far", 330, ppm * 0.55, 150)
    tile_layer(c, "bg_hills_near", 300, ppm * 0.6, 900)
    # ground plane (squashed to suggest the oblique camera), hazed toward the horizon
    g = img("ground_meadow")
    gm = MAN["ground_meadow"]["height"] * ppm
    gt = g.resize((int(gm), int(gm * 0.5)), Image.LANCZOS)
    gy0 = 590
    ground = Image.new("RGBA", (W, H - gy0))
    for yy in range(0, ground.height, gt.height):
        for xx in range(0, W, gt.width):
            ground.alpha_composite(gt, (xx, yy))
    ga = np.asarray(ground).astype(float)
    t = np.linspace(0, 1, ground.height)[:, None]
    haze = np.clip(1 - t / 0.4, 0, 1) * 0.45
    ga[..., :3] = ga[..., :3] * (1 - haze[..., None]) + np.array([214, 224, 214]) * haze[..., None]
    ga[..., 3] = 255 * np.clip(t / 0.05, 0, 1)
    c.alpha_composite(Image.fromarray(ga.astype(np.uint8), "RGBA"), (0, gy0))
    # path + flower decals laid on the ground
    path = img("decal_path_dirt")
    pw = int(ppm * 8)
    path = path.resize((pw, int(pw * path.height / path.width * 0.5)), Image.LANCZOS)
    for xx in range(-100, W, pw):
        c.alpha_composite(path, (xx, 735))
    fl = img("decal_flowers").resize((int(ppm * 3), int(ppm * 1.5)), Image.LANCZOS)
    c.alpha_composite(fl, (1180, 800))
    # props (back to front)
    place(c, "prop_tree_pine", 70, 640, ppm)
    place(c, "prop_windmill", 1480, 628, ppm)
    place(c, "prop_tree_oak", 1250, 660, ppm)
    place(c, "prop_cottage_a", 300, 668, ppm)
    place(c, "prop_inn", 760, 662, ppm)
    place(c, "prop_tree_birch", 1060, 676, ppm)
    place(c, "prop_bush_b", 520, 690, ppm)
    place(c, "prop_well", 1120, 742, ppm)
    place(c, "prop_fence", 470, 728, ppm)
    place(c, "prop_lamp_post", 600, 770, ppm)
    place(c, "prop_signpost", 960, 780, ppm)
    place(c, "prop_shop_stall", 1360, 760, ppm)
    place(c, "prop_barrel", 1450, 790, ppm)
    place(c, "prop_spirit_lantern", 180, 790, ppm)
    # party (facing right) and a few foes (flipped to face left)
    for i, k in enumerate(("char_paladin", "char_warrior", "char_mage", "char_priest", "char_hunter", "char_shaman")):
        place(c, k, 330 + i * 78 + (i % 2) * 10, 852 + (i % 2) * 12, ppm)
    place(c, "pet_wolf", 260, 872, ppm)
    place(c, "cr_mossling", 960, 860, ppm, flip=True)
    place(c, "cr_mossling_shaman", 1040, 850, ppm, flip=True)
    place(c, "cr_wolf", 1150, 872, ppm, flip=True)
    place(c, "cr_bandit", 1240, 860, ppm, flip=True)
    # foreground at the bottom edge (extra parallax: drawn a little larger)
    place(c, "fg_grass_a", 90, 930, ppm * 1.3, shadow=False)
    place(c, "fg_flowers_a", 760, 935, ppm * 1.25, shadow=False)
    place(c, "fg_stones_b", 1500, 930, ppm * 1.3, shadow=False)
    save(c, "env_composite.png")


def env_layers():
    rows = [k for k in MAN if MAN[k]["category"] == "Background"]
    W = 1600
    hs = []
    ims = []
    for k in rows:
        im = img(k)
        w = W // 2
        h = int(im.height * w / im.width)
        im = im.resize((w, h), Image.LANCZOS)
        both = Image.new("RGBA", (W, h))
        both.alpha_composite(im, (0, 0))
        both.alpha_composite(im, (w, 0))
        ims.append((k, both))
    grounds = [k for k in MAN if MAN[k]["category"] == "Ground"]
    decals = [k for k in MAN if k.startswith("decal_")]
    gh = 260
    H = sum(i.height + 26 for _, i in ims) + gh + 40 + 220
    c = sky(W, H, top=(160, 190, 214), mid=(196, 214, 228), hor=(236, 226, 206))
    d = ImageDraw.Draw(c)
    f = font(15)
    y = 4
    for k, im in ims:
        c.alpha_composite(im, (0, y + 20))
        d.text((8, y + 2), k + "  (tiled twice: the seam is in the middle)", fill=(40, 34, 52), font=f)
        d.line([(W // 2, y + 20), (W // 2, y + 20 + 8)], fill=(200, 60, 60), width=2)
        y += im.height + 26
    x = 0
    for k in grounds:
        g = img(k).resize((gh // 2, gh // 2), Image.LANCZOS)
        t = Image.new("RGBA", (gh, gh))
        for i in range(2):
            for j in range(2):
                t.alpha_composite(g, (i * gh // 2, j * gh // 2))
        c.alpha_composite(t, (x, y + 22))
        d.text((x + 4, y + 2), k + " (2x2)", fill=(40, 34, 52), font=f)
        x += gh + 12
    y += gh + 40
    x = 0
    for k in decals:
        im = img(k)
        h = 200
        im = im.resize((int(im.width * h / im.height), h), Image.LANCZOS)
        c.alpha_composite(im, (x, y + 18))
        d.text((x + 4, y), k, fill=(40, 34, 52), font=f)
        x += im.width + 10
    save(c, "env_layers.png")


def grid_sheet(keys_, name, cell=(200, 220), cols=8, bg=(226, 218, 200), label=True, uniform=None):
    cw, ch = cell
    rows = int(math.ceil(len(keys_) / float(cols)))
    c = Image.new("RGBA", (cols * cw, rows * ch), bg + (255,))
    d = ImageDraw.Draw(c)
    f = font(12)
    for i, k in enumerate(keys_):
        im = img(k)
        x0, y0 = (i % cols) * cw, (i // cols) * ch
        if uniform:
            ppm = uniform
            h = int(MAN[k]["height"] * ppm)
            w = int(im.width * h / im.height)
            if w > cw - 8 or h > ch - 22:
                s = min((cw - 8) / w, (ch - 22) / h)
                w, h = int(w * s), int(h * s)
        else:
            s = min((cw - 8) / im.width, (ch - 22) / im.height)
            w, h = int(im.width * s), int(im.height * s)
        im = im.resize((max(1, w), max(1, h)), Image.LANCZOS)
        c.alpha_composite(im, (x0 + (cw - w) // 2, y0 + ch - 18 - h))
        if label:
            d.text((x0 + 4, y0 + ch - 16), k, fill=(50, 40, 60), font=f)
    save(c, name)


def lineup(keys_, name, ppm=150, gap=8, bg=(232, 224, 206), portraits=True):
    sprites = [scaled(k, ppm) for k in keys_]
    W = sum(s.width for s in sprites) + gap * (len(sprites) + 1)
    top = max(s.height for s in sprites)
    ph = 128 if portraits else 0
    H = top + ph + 60
    c = Image.new("RGBA", (W, H), bg + (255,))
    d = ImageDraw.Draw(c)
    f = font(13)
    x = gap
    base = top + 10
    d.rectangle([0, base - 6, W, base + 2], fill=(214, 204, 184, 255))
    for k, s in zip(keys_, sprites):
        e = MAN[k]
        oy = int(base - s.height * (1 - e["pivot"][1]))
        c.alpha_composite(s, (x, oy))
        if portraits:
            pk = "portrait_" + k.split("_", 1)[1]
            if pk in MAN:
                p = img(pk).resize((112, 112), Image.LANCZOS)
                px = x + (s.width - 112) // 2
                c.alpha_composite(p, (px, base + 12))
        d.text((x + 4, H - 20), k, fill=(50, 40, 60), font=f)
        x += s.width + gap
    save(c, name)


SCHOOL = [(196, 72, 60), (90, 140, 210), (150, 100, 200), (230, 190, 90), (100, 170, 90), (120, 90, 140), (90, 90, 110)]


def icons_sheet():
    glyphs = [k for k in MAN if k.startswith("glyph_")] + [k for k in MAN if k.startswith("crest_")]
    cols = 16
    cell = 76
    rows = int(math.ceil(len(glyphs) / float(cols)))
    fxk = [k for k in MAN if MAN[k]["category"] == "Effect"]
    H = rows * (cell + 12) + 200
    c = Image.new("RGBA", (cols * cell, H), (38, 34, 48, 255))
    d = ImageDraw.Draw(c)
    f = font(10)
    for i, k in enumerate(glyphs):
        x, y = (i % cols) * cell, (i // cols) * (cell + 12)
        col = SCHOOL[i % len(SCHOOL)]
        frame = Image.new("RGBA", (60, 60), col + (255,))
        fd = ImageDraw.Draw(frame)
        fd.rectangle([0, 0, 59, 59], outline=(250, 236, 200, 255), width=2)
        c.alpha_composite(frame, (x + 8, y + 4))
        g = img(k).resize((48, 48), Image.LANCZOS)
        c.alpha_composite(g, (x + 14, y + 10))
        d.text((x + 3, y + 66), k.split("_", 1)[1][:12], fill=(220, 214, 230), font=f)
    y0 = rows * (cell + 12) + 10
    x = 10
    tints = [(255, 200, 120), (140, 200, 255), (190, 150, 255), (150, 255, 170), (255, 160, 150)]
    for i, k in enumerate(fxk):
        im = img(k)
        s = min(170 / im.width, 170 / im.height)
        im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
        a = np.asarray(im).astype(float)
        if k != "fx_shadow":
            a[..., :3] = a[..., :3] * np.array(tints[i % len(tints)]) / 255.0
        im = Image.fromarray(a.astype(np.uint8), "RGBA")
        if x + im.width > c.width:
            break
        c.alpha_composite(im, (x, y0 + (170 - im.height) // 2))
        d.text((x, y0 + 172), k, fill=(220, 214, 230), font=f)
        x += max(im.width, 70) + 12
    save(c, "icons.png")


def main():
    global MAN
    MAN = load_manifest()
    print("writing previews to", K.PREVIEW_DIR)
    env_composite()
    env_layers()
    props = [k for k in MAN if MAN[k]["category"] == "Prop" and not k.startswith("decal_")] + \
            [k for k in MAN if MAN[k]["category"] == "Foreground"]
    grid_sheet(props, "props.png", cell=(200, 210), cols=9)
    heroes = [k for k in MAN if k.startswith("char_")]
    comps = [k for k in MAN if k.startswith("comp_")]
    lineup(heroes + comps, "characters.png", ppm=118)
    lineup([k for k in MAN if k.startswith("npc_")], "npcs.png", ppm=150)
    cre = [k for k in MAN if MAN[k]["category"] == "Creature"]
    grid_sheet(cre, "creatures.png", cell=(230, 250), cols=8)
    icons_sheet()
    return 0


if __name__ == "__main__":
    sys.exit(main())
