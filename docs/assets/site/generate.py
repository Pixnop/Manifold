"""Generate the Manifold documentation site artwork.

Everything here is procedural and seeded, so a rerun reproduces the same files:

    python3 docs/assets/site/generate.py

This reuses the voxel renderer built for the Mod DB page (docs/assets/moddb/generate.py) instead
of duplicating it: Scene, vox, cube, island, gate, arcs_layer, nebula, the palette, and the rest
of the drawing machinery come from there. Only the site-specific scenes (hero layers, highlight
loops, article headers, divider, 404) live in this file.

Needs Pillow and NumPy, same as the Mod DB generator.
"""

import importlib.util
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

SITE_DIR = Path(__file__).resolve().parent

# Import the Mod DB renderer by path, under its own module name: both scripts are called
# generate.py, and a plain `import generate` would collide with this module in sys.modules.
_spec = importlib.util.spec_from_file_location("moddb_generate", SITE_DIR.parent / "moddb" / "generate.py")
moddb = importlib.util.module_from_spec(_spec)
sys.modules["moddb_generate"] = moddb
_spec.loader.exec_module(moddb)

moddb.OUT = SITE_DIR  # redirect the reused animated()/save calls to this directory
OUT = SITE_DIR
R = moddb.R

VOID, DEEP, SLATE, STONE = moddb.VOID, moddb.DEEP, moddb.SLATE, moddb.STONE
SLATE_MID, SLATE_LIGHT = moddb.SLATE_MID, moddb.SLATE_LIGHT
SALMON, SALMON_LIGHT, GREEN, ICE, EARTH, WHITE = (
    moddb.SALMON, moddb.SALMON_LIGHT, moddb.GREEN, moddb.ICE, moddb.EARTH, moddb.WHITE)
mix, vox, Scene, island, gate = moddb.mix, moddb.vox, moddb.Scene, moddb.island, moddb.gate
arcs_layer, bezier, bob = moddb.arcs_layer, moddb.bezier, moddb.bob
CARD, card, union_box = moddb.CARD, moddb.card, moddb.union_box


# ---------------------------------------------------------------- a small voxel traveller

def traveller(i, j, k, cloak=SALMON_LIGHT, facing=1.0, alpha=255, size=1.0):
    """A tiny blocky figure: feet, a cloak, a head and a satchel, at (i, j, k)."""
    skin = mix(ICE, EARTH, 0.35)
    pack = mix(cloak, SLATE, 0.4)
    parts = [
        vox(i, j, k, SLATE, size=0.55 * size),
        vox(i, j, k + 0.5 * size, cloak, size=0.6 * size),
        vox(i, j, k + 1.0 * size, skin, size=0.42 * size),
        vox(i, j + 0.35 * facing * size, k + 0.45 * size, pack, size=0.32 * size),
    ]
    return [vox(v[0], v[1], v[2], v[3], alpha=alpha, size=v[5], emissive=v[6]) for v in parts]


# ---------------------------------------------------------------- hero (parallax layers)

HERO = (1440, 640)
HERO_FRAMES = 22


def hero_background(light=False):
    """Opaque nebula: lighter near the top (the lit overworld) fading to void at the bottom.

    light=True is the overworld daylight variant used in light mode: sky-blue at the top instead
    of a dim void, so the hero reads as a place, not a black rectangle, on a pale page.
    """
    w, h = HERO
    rng = np.random.default_rng(101 if not light else 102)
    clouds = moddb.nebula(rng, w, h, tile=False)
    if light:
        # nebula() is built around a dark ground; lift it toward the sky before tinting so the
        # bottom stays a legible (if deep) blue instead of near-black.
        clouds = clouds * 0.35 + np.array(mix(SLATE_LIGHT, ICE, 0.5), dtype=float) * 0.65
        top_tint = np.array(mix(ICE, WHITE, 0.5), dtype=float)
        fade = np.clip(1 - np.arange(h) / (h * 0.6), 0, 1)[:, None, None] ** 1.4
        clouds = clouds * (1 - fade * 0.7) + top_tint * (fade * 0.7)
    else:
        top_tint = np.array(mix(ICE, SALMON_LIGHT, 0.3), dtype=float)
        fade = np.clip(1 - np.arange(h) / (h * 0.5), 0, 1)[:, None, None] ** 1.6
        clouds = clouds * (1 - fade * 0.55) + top_tint * (fade * 0.55)
    bg = np.asarray(moddb.to_image(clouds).resize((w * R, h * R), Image.BICUBIC), dtype=float).copy()
    moddb.sprinkle_stars(rng, bg, 90 if light else 420, wrap=False)
    return moddb.to_image(bg).convert("RGBA")


def hero_far(t):
    """Small, distant islands drifting slowly, each on its own bob."""
    w, h = HERO
    scene = Scene(w, h)
    spots = [(160, 90, 4, 0.00), (430, 60, 3, 0.30), (980, 70, 4, 0.55),
             (1260, 110, 3, 0.80), (60, 220, 3, 0.15), (700, 40, 3, 0.65)]
    for n, (x, y, radius, phase) in enumerate(spots):
        rng = np.random.default_rng(300 + n)
        scene.add(island(rng, radius, 1, trees=rng.integers(0, 2)), x, y + bob(t, phase, 2.0), 5)
    return scene.render(bloom=5, strength=0.5)


def hero_mid(t):
    """The portal island: gate, membrane, sparks, and a traveller crossing through."""
    w, h = HERO
    rng = np.random.default_rng(2)
    gate_vox, cells = gate(-3, -2, 0, 4, 6, rng, t=t)
    base = island(np.random.default_rng(3), 6, 3, keep_clear=cells,
                  light=((0, -1), SALMON_LIGHT, 5), tree_cells=[(-4, -4), (4, -3), (4, 4)])
    oy = 330 + bob(t, 0.0, 2.0)
    scene = Scene(w, h)
    scene.add(base + gate_vox, w / 2, oy, 15)
    img = scene.render(bloom=15, strength=0.7)

    # the traveller walks up from the grass, through the opening, and fades into the membrane
    phase = t % 1.0
    if phase < 0.6:
        u = phase / 0.6
        j = 3.0 - 3.6 * u
        alpha = int(255 * min(1.0, u / 0.15) * min(1.0, (1 - u) / 0.35))
        fig = Scene(w, h)
        fig.add(traveller(0.3, j, 0.05, alpha=alpha, size=0.9), w / 2, oy, 15)
        img.alpha_composite(fig.render(bloom=0))
    return img


def hero_near(t):
    """One or two large islands, cropped by the frame, drifting a little closer to the viewer."""
    w, h = HERO
    scene = Scene(w, h)
    scene.add(island(np.random.default_rng(401), 7, 3, trees=2), -60, 520 + bob(t, 0.1, 2.5), 20)
    scene.add(island(np.random.default_rng(402), 5, 3, trees=1), 1330, -40 + bob(t, 0.5, 2.2), 17)
    return scene.render(bloom=8, strength=0.55)


# ---------------------------------------------------------------- 0.6 highlight loops

def pregeneration_scene(t):
    """A dimension's terrain rising into place with nobody there, then settling and fading out."""
    scene = Scene(260, 220)
    voxels = island(np.random.default_rng(201), 3, 2, trees=1)
    ks = [v[2] for v in voxels]
    kmin, span = min(ks), max(1, max(ks) - min(ks))
    fade = 1.0 if t < 0.86 else max(0.0, 1 - (t - 0.86) / 0.12)
    out = []
    for (i, j, k, color, alpha, size, emissive) in voxels:
        reveal = (k - kmin) / span * 0.7
        if t < reveal - 0.05:
            continue
        rise = max(0.0, (reveal - t) / 0.05)
        a = int(alpha * (1 - rise * 0.4) * fade)
        if a < 6:
            continue
        out.append(vox(i, j, k + rise * 2.2, color, alpha=a, size=size, emissive=emissive))
    scene.add(out, 130, 120, 11)  # +10 page px of headroom so the risen terrain clears the canvas top
    return scene.render(bloom=7)


def column_generated_scene(t):
    """A generated column, with a marker rising into place and settling on top of it."""
    scene = Scene(260, 220)
    ground = island(np.random.default_rng(211), 3, 1, keep_clear=set())
    phase = t % 1.0
    rise = max(0.0, 1 - phase / 0.35) if phase < 0.35 else 0.0
    settle = max(0.0, (phase - 0.35) / 0.65)
    pulse = 0.5 + 0.5 * np.sin(2 * np.pi * settle * 3)
    k = 2 + rise * 3.5
    alpha = 255 if phase > 0.04 else int(255 * phase / 0.04)
    marker = [
        vox(0, 0, k, SALMON, alpha=alpha, size=0.4, emissive=True),
        vox(0, 0, k + 0.55, mix(SALMON_LIGHT, ICE, pulse), alpha=alpha, size=0.22, emissive=True),
    ]
    scene.add(ground + marker, 130, 110, 11)
    return scene.render(bloom=9)


def visible_arcs(w, h, arcs, t=0.0, travellers=0):
    """arcs_layer(), with a soft dark halo behind it so the dashes and travellers still read
    against a light card background (the arcs' own ice/salmon tints are close to white)."""
    layer = arcs_layer(w, h, arcs, t=t, travellers=travellers)
    shadow_alpha = (np.asarray(layer, dtype=float)[..., 3:4] * 0.6)
    shadow = np.dstack([np.zeros(shadow_alpha.shape[:2] + (3,)), shadow_alpha])
    shadow_img = Image.fromarray(shadow.astype(np.uint8), "RGBA").filter(ImageFilter.GaussianBlur(2 * R))
    out = Image.new("RGBA", layer.size)
    out.alpha_composite(shadow_img)
    out.alpha_composite(layer)
    return out


def client_metadata_scene(t):
    """A tagged packet travelling along an arc from a server island to a client island."""
    scene = Scene(260, 220)
    scene.add(island(np.random.default_rng(221), 2, 1), 70, 130, 8)
    scene.add(island(np.random.default_rng(222), 2, 1, trees=1), 190, 70, 8)
    img = scene.render(bloom=6)
    arcs = [((78, 118), (182, 78), 34, ICE + (170,))]
    under = visible_arcs(260, 220, arcs, t=t, travellers=1)
    under.alpha_composite(img)
    return under


def safe_landing_scene(t):
    """A figure drops from above and lands on dry ground beside a pond, not in it."""
    scene = Scene(260, 220)
    voxels = island(np.random.default_rng(231), 3, 2, keep_clear={(1, 0), (1, 1), (2, 0)})
    water = mix(ICE, SLATE_LIGHT, 0.5)
    for (i, j) in ((1, 0), (1, 1), (2, 0)):
        voxels.append(vox(i, j, 1, water, alpha=210))
    scene.add(voxels, 130, 118, 11)
    base = scene.render(bloom=5)

    phase = t % 1.0
    falling = phase < 0.4
    fall = max(0.0, 1 - phase / 0.4) if falling else 0.0
    hop = 0.14 * abs(np.sin(np.pi * min(1.0, phase / 0.4) * 2)) if falling else 0.0
    alpha = 255 if phase > 0.02 else int(255 * phase / 0.02)
    fig = Scene(260, 220)
    fig.add(traveller(-1, -1, 1 + fall * 3.2 + hop, alpha=alpha), 130, 118, 11)
    base.alpha_composite(fig.render(bloom=0))
    return base


def schema_saves_scene(t):
    """A save chest that stays intact while its version tag glows steadily."""
    scene = Scene(260, 220)
    voxels = island(np.random.default_rng(241), 2, 2)
    pulse = 0.5 + 0.5 * np.sin(2 * np.pi * t)
    chest = [vox(0, 0, 1, EARTH, size=0.85), vox(0, 0, 1.7, mix(EARTH, (60, 42, 32), 0.5), size=0.86)]
    tag = [vox(0.55, -0.2, 2.2, mix(SALMON, ICE, pulse), size=0.28, emissive=True)]
    scene.add(voxels + chest + tag, 130, 118, 12)
    return scene.render(bloom=8)


# ---------------------------------------------------------------- article header loops

def getting_started_scene(t):
    """A portal gate assembling stone by stone, then lighting up, then fading for the next loop."""
    scene = Scene(260, 220)
    ground = island(np.random.default_rng(252), 3, 2, light=((0, -1), SALMON_LIGHT, 4))
    gate_vox, _ = gate(-2, -1, 0, 3, 5, np.random.default_rng(251), t=t)
    ks = [v[2] for v in gate_vox]
    kmin, span = min(ks), max(1, max(ks) - min(ks))
    fade = 1.0 if t < 0.86 else max(0.0, 1 - (t - 0.86) / 0.12)
    out = []
    for (i, j, k, color, alpha, size, emissive) in gate_vox:
        reveal = (k - kmin) / span * 0.5 + (0.25 if emissive else 0.0)
        if t < reveal - 0.06:
            continue
        rise = max(0.0, (reveal - t) / 0.06) if not emissive else 0.0
        a = int(alpha * (1 - rise * 0.5) * fade)
        if a < 6:
            continue
        out.append(vox(i, j, k + rise * 2.2, color, alpha=a, size=size, emissive=emissive))
    scene.add(ground + out, 130, 130, 11)  # +10 page px of headroom so the gate top clears the canvas
    return scene.render(bloom=10)


def dimensions_scene(t):
    """Two settled islands and a third, ephemeral one fading in and out, linked by an arc."""
    scene = Scene(260, 220)
    scene.add(island(np.random.default_rng(261), 2, 1, trees=1), 75, 130, 7)
    scene.add(island(np.random.default_rng(262), 2, 1), 185, 55, 7)
    ephemeral = island(np.random.default_rng(263), 2, 1, trees=1)
    env = 0.15 + 0.85 * (0.5 - 0.5 * np.cos(2 * np.pi * t))
    eph = [vox(i, j, k, color, alpha=int(alpha * env), size=size, emissive=emissive)
           for (i, j, k, color, alpha, size, emissive) in ephemeral]
    scene.add(eph, 130, 188, 6)
    img = scene.render(bloom=8)
    under = visible_arcs(260, 220, [((80, 120), (128, 178), 18, ICE + (140,))], t=t)
    under.alpha_composite(img)
    return under


def architecture_scene(t):
    """Three islands linked by transit arcs, with a pulse riding each one."""
    scene = Scene(260, 220)
    scene.add(island(np.random.default_rng(271), 2, 1), 60, 130, 6)
    scene.add(island(np.random.default_rng(272), 2, 1, trees=1), 150, 55, 6)
    scene.add(island(np.random.default_rng(273), 2, 1), 205, 150, 6)
    img = scene.render(bloom=6)
    arcs = [
        ((66, 120), (150, 68), 22, ICE + (160,)),
        ((156, 68), (206, 140), 20, SALMON_LIGHT + (160,)),
        ((66, 120), (206, 140), 30, ICE + (110,)),
    ]
    under = visible_arcs(260, 220, arcs, t=t, travellers=1)
    under.alpha_composite(img)
    return under


# ---------------------------------------------------------------- divider

def divider_line():
    w, h = 800, 28
    line = Image.new("RGBA", (w * R, h * R))
    px = line.load()
    for x in range(w * R):
        taper = 1 - abs(x - w * R / 2) / (w * R / 2)
        color = mix(SALMON, ICE, x / (w * R))
        for y, a in ((12, 0.45), (13, 1.0), (14, 1.0), (15, 0.45)):
            for sub in range(R):
                px[x, y * R + sub] = color + (int(255 * a * taper ** 1.2),)
    out = line.filter(ImageFilter.GaussianBlur(3 * R))
    out.alpha_composite(line)
    return out


def divider_frame(t, line):
    """A single spark travelling left to right, fading in and out at the ends so the loop hides its seam."""
    w, h = 800, 28
    phase = t % 1.0
    x = w * (0.04 + 0.92 * phase)
    alpha = int(255 * np.sin(np.pi * phase) ** 0.6)
    color = mix(SALMON, ICE, phase)
    scene = Scene(w, h)
    scene.add([vox(0, 0, 0, color, alpha=alpha, emissive=True)], x, 14, 6)
    out = line.copy()
    out.alpha_composite(scene.render(bloom=6))
    return out


# ---------------------------------------------------------------- 404

def closed_gate_voxels(rng):
    """The gate's stonework, dimmed but still legible (not all the way to void), sparks dropped."""
    gate_vox, cells = gate(-1, -1, 0, 2, 4, rng, t=0.0)
    out = []
    for (i, j, k, color, alpha, size, emissive) in gate_vox:
        if not emissive:
            out.append(vox(i, j, k, color, alpha, size, False))
        elif size < 0.28:
            continue  # drifting sparks: none, the gate is shut
        else:
            out.append(vox(i, j, k, mix(color, VOID, 0.4), alpha=190, size=size))
    return out


def not_found_scene(t):
    """A lone figure drifting in the void near a closed gate: the 404 page's whole scene, so it
    renders large (see NOT_FOUND_SIZE below) rather than at card thumbnail size."""
    scene = Scene(340, 260)
    perch_bob = bob(t, 0.2, 3.0)
    scene.add(island(np.random.default_rng(283), 1, 1), 215, 175 + perch_bob, 8)
    scene.add(closed_gate_voxels(np.random.default_rng(282)), 120, 138, 13)
    img = scene.render(bloom=6)
    fig = Scene(340, 260)
    # size=1.3 rendered at roughly 10 page px and sat off to one side; 2.4 reads as a figure at
    # this scene's own scale, closer to the gate so the composition centres as one group.
    fig.add(traveller(0, -1.6, 1.3 + bob(t, 0.0, 1.9), size=2.4), 215, 175 + perch_bob, 8)
    img.alpha_composite(fig.render(bloom=0))
    return img


NOT_FOUND_SIZE = (320 * R, 233 * R)


# ---------------------------------------------------------------- island underside strip

UNDERSIDE_COLS = 16
UNDERSIDE_SIZE = (UNDERSIDE_COLS * 3 + 16, 30)  # fixed canvas: no bbox crop, so the aspect ratio
                                                 # main.css draws it at is known exactly, and the
                                                 # margin below keeps every column clear of the
                                                 # canvas edge (nothing cut mid-block)


def island_underside(seed=51):
    """A single full-width underside band, not a repeat-x tile: a grass lip over dirt and stone
    that tapers shallower at both ends and deepest in the middle, so it reads as the hanging root
    of one island instead of a row of repeated bricks. Used once per card, at its true aspect
    ratio (UNDERSIDE_SIZE), not stretched to a fixed background-size."""
    rng = np.random.default_rng(seed)
    scene = Scene(*UNDERSIDE_SIZE)
    voxels = []
    mid = (UNDERSIDE_COLS - 1) / 2
    for i in range(UNDERSIDE_COLS):
        taper = 1 - (abs(i - mid) / (mid + 1)) ** 1.6  # 1.0 at the centre, ->0 at both ends
        depth = max(1, min(4, round(taper * 3.4 + rng.uniform(-0.4, 0.4))))
        for j in range(2):
            voxels.append(vox(i, j, 0, GREEN))
        for k in range(1, depth + 1):
            color = EARTH if k == 1 else (STONE if k < depth else mix(STONE, VOID, 0.4))
            for j in range(2):
                voxels.append(vox(i, j, -k, color))
    scene.add(voxels, 8, 5, 3.0)
    return scene.render(bloom=0)


# ---------------------------------------------------------------- API reference card art

def api_reference_art():
    """A book on a stand, on its own small island - the "browse the docs" grid's odd one out."""
    scene = Scene(260, 220)
    ground = island(np.random.default_rng(291), 3, 2, keep_clear={(0, 0), (0, 1)})
    book = [
        vox(0, 0, 1, EARTH, size=0.85),
        vox(0, 0.3, 1.2, mix(EARTH, (60, 42, 32), 0.5), size=0.5),
        vox(0, 0, 1.55, mix(ICE, WHITE, 0.25), size=0.95),
        vox(0.32, -0.05, 1.85, SALMON_LIGHT, size=0.3, emissive=True),
    ]
    scene.add(ground + book, 130, 118, 11)
    img = scene.render(bloom=8)
    box = img.getbbox()
    return card(img, box) if box else img


# ---------------------------------------------------------------- drifting background islands

def drift_island(seed, radius=2, trees=0):
    """A small standalone island for the landing's between-section scroll parallax."""
    scene = Scene(160, 120)
    scene.add(island(np.random.default_rng(seed), radius, 1, trees=trees), 80, 55, 9)
    img = scene.render(bloom=4, strength=0.5)
    box = img.getbbox()
    return img.crop(box) if box else img


# ---------------------------------------------------------------- driver
#
# animated() (reused from moddb) is a fine fit for the card-sized loops at its own defaults, but
# the hero layers and a couple of the busier card loops need a lower quality/frame budget to stay
# inside the file-weight limits, so this local saver adds that knob without touching the shared
# renderer. It also folds the last few frames back toward frame 0 (xfade) so the loop point is a
# blend instead of a jump, and can emit a half-size companion for srcset on narrow viewports.

def save_animated(name, frame_at, still_t, frames, quality=76, crop=True, size=None, xfade=0.08, mobile=False):
    frame_imgs = [frame_at(n / frames) for n in range(frames)]
    if xfade:
        span = max(1, int(frames * xfade))
        for k in range(span):
            idx = frames - span + k
            weight = 0.75 * (k + 1) / span
            frame_imgs[idx] = Image.blend(frame_imgs[idx].convert("RGBA"), frame_imgs[0].convert("RGBA"), weight)
    if crop:
        box = union_box(frame_imgs)
        size = size or CARD
        frame_imgs = [card_sized(f, box, size) for f in frame_imgs]
        still = card_sized(frame_at(still_t), box, size)
    else:
        still = frame_at(still_t)
    duration = frame_ms or moddb.FRAME_MS
    still.save(OUT / f"{name}.png", optimize=True)
    frame_imgs[0].save(OUT / f"{name}.webp", save_all=True, append_images=frame_imgs[1:],
                       duration=duration, loop=0, quality=quality, method=6)
    if mobile:
        half = [f.resize((f.width // 2, f.height // 2), Image.LANCZOS) for f in frame_imgs]
        half[0].save(OUT / f"{name}-720.webp", save_all=True, append_images=half[1:],
                     duration=duration, loop=0, quality=quality, method=6)


def card_sized(art, box, size):
    """Same crop-and-centre as moddb.card(), on a caller-chosen canvas instead of the fixed
    220x160 CARD (the 404 scene and a couple of others want a bigger frame).

    If the cropped content is taller or wider than the target canvas, alpha_composite would still
    happily paste it at a negative offset - centred, but with the excess sliced off flush at both
    edges (a scene's own bottom or top touching the canvas border, not free-floating the way every
    other island in this set does). Scaling down to fit first, instead, keeps every frame clear of
    all four edges - checked by the assert below, since a scene changed later could reintroduce
    the same overflow."""
    left, top, right, bottom = box
    m = 6 * R
    art = art.crop((max(0, left - m), max(0, top - m), min(art.width, right + m), min(art.height, bottom + m)))
    # fit within size minus a couple of R-scaled px, not size itself: a scale that exactly fills
    # one dimension would otherwise leave that pair of edges flush against the canvas border, no
    # transparent margin left to clear assert_edges_clear below.
    fit_w, fit_h = size[0] - 2 * R, size[1] - 2 * R
    scale = min(1.0, fit_w / art.width, fit_h / art.height)
    if scale < 1.0:
        art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.LANCZOS)
    out = Image.new("RGBA", size)
    out.alpha_composite(art, ((size[0] - art.width) // 2, (size[1] - art.height) // 2))
    assert_edges_clear(out)
    return out


def assert_edges_clear(img):
    """Every card frame should float free of its own canvas: row/column 0 and the last row/column
    transparent. Catches a scene whose content silently touches or crosses the frame edge (the
    article-header and highlight-loop bug this originally shipped with)."""
    px = img.load()
    w, h = img.size
    for x in range(w):
        assert px[x, 0][3] == 0 and px[x, h - 1][3] == 0, f"content touches top/bottom edge at x={x}"
    for y in range(h):
        assert px[0, y][3] == 0 and px[w - 1, y][3] == 0, f"content touches left/right edge at y={y}"


if __name__ == "__main__":
    # hero: four aligned layers at HERO size, no cropping, so they stack without drifting.
    # The opaque background is fully covered every frame, so a lossy webp costs far less than
    # the old PNG for the same look; a half-size companion covers the hero's phone crop.
    for light, suffix in ((False, ""), (True, "-light")):
        bg = hero_background(light=light)
        bg.convert("RGB").save(OUT / f"hero-bg{suffix}.webp", quality=82, method=6)
        bg.resize((bg.width // 2, bg.height // 2), Image.LANCZOS).convert("RGB").save(
            OUT / f"hero-bg{suffix}-720.webp", quality=82, method=6)
    # all three share HERO_FRAMES's period so the parallax layers stay in phase with each other
    # over repeated loops
    save_animated("hero-far", hero_far, 0.0, frames=HERO_FRAMES, quality=42, crop=False, mobile=True)
    save_animated("hero-mid", hero_mid, 0.0, frames=HERO_FRAMES, quality=40, crop=False, mobile=True)
    save_animated("hero-near", hero_near, 0.0, frames=HERO_FRAMES, quality=42, crop=False, mobile=True)

    save_animated("highlight-pregeneration", pregeneration_scene, 0.9, frames=36, quality=80)
    save_animated("highlight-column-generated", column_generated_scene, 0.6, frames=36)
    save_animated("highlight-client-metadata", client_metadata_scene, 0.5, frames=36)
    save_animated("highlight-safe-landing", safe_landing_scene, 0.7, frames=36)
    save_animated("highlight-schema-saves", schema_saves_scene, 0.25, frames=36)

    # quality dropped from the high-70s to 56-58: card_sized's edge-clearance fix (above) grew
    # these frames' opaque area versus the old clipped crop, which pushed every one of them past
    # the README's 270 KB card-loop budget at the old quality; card art has none of the hero
    # background's fine star/nebula grain lossy compression would show up on, so the drop costs
    # little visually for a real file-weight win.
    save_animated("header-getting-started", getting_started_scene, 0.9, frames=36, quality=58)
    save_animated("header-dimensions", dimensions_scene, 0.25, frames=36, quality=58)
    save_animated("header-worldgen", moddb.worldgen_scene, 0.8, frames=42, quality=56)
    save_animated("header-transit-and-travel-policy", moddb.transit_scene, 0.3, frames=36, quality=56)
    save_animated("header-architecture", architecture_scene, 0.4, frames=30, quality=56)

    line = divider_line()
    save_animated("divider", lambda t: divider_frame(t, line), 0.4, frames=30, crop=False, xfade=0)

    save_animated("not-found", not_found_scene, 0.5, frames=32, size=NOT_FOUND_SIZE)

    island_underside().save(OUT / "island-underside.png", optimize=True)
    api_reference_art().save(OUT / "api-reference.png", optimize=True)
    for n, (seed, radius, trees) in enumerate(((61, 2, 1), (62, 1, 0), (63, 2, 0))):
        drift_island(seed, radius, trees).save(OUT / f"drift-{n + 1}.png", optimize=True)

    for f in sorted(OUT.glob("*.png")) + sorted(OUT.glob("*.webp")):
        img = Image.open(f)
        print(f.name, img.size, f.stat().st_size // 1024, "KB")
