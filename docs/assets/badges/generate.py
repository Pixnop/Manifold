#!/usr/bin/env python3
"""Generator for the "Requires Manifold" badge kit.

Five formats (flat, plaque, square, seal, wide) times three palettes (void, light, slate), each
as a .png at 1x and 2x. The mark is the portal island of the site's hero, drawn by the same
voxel renderer as docs/assets/moddb; the wordmarks are set in Sora, the site's heading font.

    python3 docs/assets/badges/generate.py /path/to/Sora[wght].ttf

Sora is not in this repository (OFL, https://fonts.google.com/specimen/Sora). Output goes next
to this script, overwriting the existing badge files.
"""
import importlib.util
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("moddb_generate", OUT.parent / "moddb" / "generate.py")
moddb = importlib.util.module_from_spec(_spec)
sys.modules["moddb_generate"] = moddb
_spec.loader.exec_module(moddb)

FONT_PATH = sys.argv[1] if len(sys.argv) > 1 else "Sora[wght].ttf"
U = 8  # working pixels per page pixel: the 2x output, supersampled 4 times
LINE_1, LINE_2 = "REQUIRES", "MANIFOLD"
TAGLINE = "Custom dimensions for Vintage Story"

# bg/edge/ink drive the framed formats, accent is the second colour of rules and of the small
# line; label_*/msg_* are the two chips of the flat format.
PALETTES = {
    "void": dict(bg=(21, 22, 24), edge=(92, 123, 137), ink=(213, 237, 240), accent=(226, 143, 112),
                 label_bg=(33, 40, 46), label_fg=(213, 237, 240), msg_bg=(226, 143, 112), msg_fg=(30, 20, 16)),
    "light": dict(bg=(244, 247, 248), edge=(51, 63, 72), ink=(51, 63, 72), accent=(176, 88, 66),
                  label_bg=(51, 63, 72), label_fg=(246, 250, 250), msg_bg=(226, 143, 112), msg_fg=(30, 20, 16)),
    "slate": dict(bg=(51, 63, 72), edge=(213, 237, 240), ink=(246, 250, 250), accent=(226, 161, 145),
                  label_bg=(63, 71, 78), label_fg=(246, 250, 250), msg_bg=(213, 237, 240), msg_fg=(21, 22, 24)),
}


def render_mark():
    """The portal island, cropped to its content, large enough to be scaled down for any badge."""
    gate, cells = moddb.gate(-2, -1, 0, 3, 5, np.random.default_rng(2), t=0.3)
    gate = [v for v in gate if not (v[5] < 0.3 and v[4] < 250)]  # no sparks: noise at badge size
    base = moddb.island(np.random.default_rng(7), 4, 3, keep_clear=cells,
                        light=((0, 0), moddb.SALMON_LIGHT, 4), tree_cells=[(3, 2), (-3, 3)])
    scene = moddb.Scene(360, 360)
    scene.add(base + gate, 180, 190, 20)
    img = scene.render(bloom=8, strength=0.45)
    return img.crop(img.getbbox())


MARK = render_mark()


def mark(height):
    """The mark scaled to `height` page px."""
    h = round(height * U)
    return MARK.resize((round(MARK.width * h / MARK.height), h), Image.LANCZOS)


def font(size, weight):
    f = ImageFont.truetype(FONT_PATH, round(size * U))
    f.set_variation_by_axes([weight])
    return f


def text_width(text, f, spacing):
    return sum(f.getlength(c) for c in text) / U + spacing * (len(text) - 1)


def draw_text(img, x, baseline, text, f, fill, spacing=0.0):
    """Left-aligned text on a baseline, with letter spacing (all in page px)."""
    draw = ImageDraw.Draw(img)
    for c in text:
        draw.text((x * U, baseline * U), c, font=f, fill=fill, anchor="ls")
        x += f.getlength(c) / U + spacing


def draw_arc_text(img, cx, cy, radius, text, f, fill, spacing):
    """Text centred on the top of a circle, its baseline on the circle."""
    advances = [f.getlength(c) / U + spacing for c in text]
    angle = -math.pi / 2 - (sum(advances) - spacing) / radius / 2
    box = round(f.size * 3)
    for c, adv in zip(text, advances):
        mid = angle + (adv - spacing) / radius / 2
        glyph = Image.new("RGBA", (box, box))
        ImageDraw.Draw(glyph).text((box / 2, box / 2), c, font=f, fill=fill, anchor="ms")
        glyph = glyph.rotate(-math.degrees(mid) - 90, resample=Image.BICUBIC)
        px, py = (cx + radius * math.cos(mid)) * U, (cy + radius * math.sin(mid)) * U
        img.alpha_composite(glyph, (round(px - box / 2), round(py - box / 2)))
        angle += adv / radius


def framed(w, h, p, radius=6):
    """An opaque rounded ground with a double frame, the kit's shared look."""
    img = Image.new("RGBA", (w * U, h * U))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((0, 0, w * U - 1, h * U - 1), radius * U, fill=p["bg"])
    draw.rounded_rectangle((2 * U, 2 * U, (w - 2) * U - 1, (h - 2) * U - 1), (radius - 2) * U,
                           outline=p["edge"], width=U)
    draw.rounded_rectangle((4.5 * U, 4.5 * U, (w - 4.5) * U - 1, (h - 4.5) * U - 1), (radius - 3.5) * U,
                           outline=p["edge"] + (110,), width=U // 2)
    return img


def paste_mark(img, m, x, y):
    img.alpha_composite(m, (round(x * U), round(y * U)))


def flat(p):
    f_label, f_msg = font(10.5, 500), font(10.5, 700)
    m = mark(15)
    label, msg = "requires", "Manifold"
    left = 5 + m.width / U + 4 + text_width(label, f_label, 0.2) + 6
    w = math.ceil(left + 6 + text_width(msg, f_msg, 0.2) + 7)
    img = Image.new("RGBA", (w * U, 20 * U))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((0, 0, w * U - 1, 20 * U - 1), 3 * U, fill=p["msg_bg"])
    draw.rounded_rectangle((0, 0, left * U, 20 * U - 1), 3 * U, fill=p["label_bg"])
    draw.rectangle(((left - 3) * U, 0, left * U, 20 * U - 1), fill=p["label_bg"])
    paste_mark(img, m, 5, 2.5)
    draw_text(img, 5 + m.width / U + 4, 14, label, f_label, p["label_fg"], 0.2)
    draw_text(img, left + 6, 14, msg, f_msg, p["msg_fg"], 0.2)
    return img


def plaque(p):
    w, h = 220, 60
    img = framed(w, h, p)
    m = mark(44)
    f1, f2 = font(9.5, 600), font(19, 800)
    block = m.width / U + 12 + max(text_width(LINE_1, f1, 2.6), text_width(LINE_2, f2, 2.2))
    x = (w - block) / 2
    paste_mark(img, m, x, (h - 44) / 2)
    x += m.width / U + 12
    draw_text(img, x, 25, LINE_1, f1, p["accent"], 2.6)
    draw_text(img, x, 46, LINE_2, f2, p["ink"], 2.2)
    return img


def square(p):
    w = h = 160
    img = framed(w, h, p, radius=8)
    m = mark(82)
    paste_mark(img, m, (w - m.width / U) / 2, 14)
    f1, f2 = font(9.5, 600), font(18, 800)
    draw_text(img, (w - text_width(LINE_1, f1, 2.6)) / 2, 118, LINE_1, f1, p["accent"], 2.6)
    draw_text(img, (w - text_width(LINE_2, f2, 2.2)) / 2, 140, LINE_2, f2, p["ink"], 2.2)
    return img


def seal(p):
    w = h = 112
    img = Image.new("RGBA", (w * U, h * U))
    draw = ImageDraw.Draw(img)
    draw.ellipse((0, 0, w * U - 1, h * U - 1), fill=p["bg"])
    draw.ellipse((2 * U, 2 * U, (w - 2) * U - 1, (h - 2) * U - 1), outline=p["edge"], width=U)
    draw.ellipse((4.5 * U, 4.5 * U, (w - 4.5) * U - 1, (h - 4.5) * U - 1), outline=p["edge"] + (110,), width=U // 2)
    draw_arc_text(img, w / 2, h / 2, 39, f"{LINE_1} {LINE_2}", font(8.6, 700), p["ink"], 1.5)
    m = mark(50)
    paste_mark(img, m, (w - m.width / U) / 2, 37)
    for dx in (-9, 0, 9):  # three voxel pips closing the ring under the mark
        s = 2.2 if dx == 0 else 1.6
        cx, cy = (w / 2 + dx) * U, 98 * U
        draw.polygon([(cx, cy - s * U), (cx + s * U, cy), (cx, cy + s * U), (cx - s * U, cy)], fill=p["accent"])
    return img


def wide(p):
    w, h = 600, 100
    img = framed(w, h, p, radius=8)
    m = mark(74)
    paste_mark(img, m, 26, (h - 74) / 2)
    x = 26 + m.width / U + 22
    f1, f2 = font(26, 800), font(12.5, 500)
    draw_text(img, x, 52, f"{LINE_1} {LINE_2}", f1, p["ink"], 2.4)
    draw = ImageDraw.Draw(img)
    draw.rectangle((x * U, 61 * U, (x + 44) * U, 62.5 * U), fill=p["accent"])
    draw_text(img, x, 80, TAGLINE, f2, p["accent"], 0.6)
    return img


FORMATS = {"flat": flat, "plaque": plaque, "square": square, "seal": seal, "wide": wide}

if __name__ == "__main__":
    for fmt, build in FORMATS.items():
        for name, palette in PALETTES.items():
            img = build(palette)
            w, h = img.width // U, img.height // U
            img.resize((w * 2, h * 2), Image.LANCZOS).save(OUT / f"requires-manifold-{fmt}-{name}@2x.png", optimize=True)
            img.resize((w, h), Image.LANCZOS).save(OUT / f"requires-manifold-{fmt}-{name}.png", optimize=True)
            print(fmt, name, f"{w}x{h}")
