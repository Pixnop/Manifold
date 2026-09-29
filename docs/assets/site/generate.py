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
from PIL import Image, ImageDraw, ImageFilter

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
#
# Each island used to be baked into a full-canvas animated webp, 22 frames at 220ms so it stayed
# in phase with the other two layers - a slow bob, but only 4.5 frames per second of it, which
# read as choppy. The bob is a small pure translation, so it is a CSS keyframe now (mf-hero-bob
# in main.css) and every island is its own tightly-cropped static image instead: (x, y) below is
# its un-bobbed centre in the 1440x640 hero, in page px, and phase/amp are what main.css's
# animation-delay and --mf-bob need to reproduce the same drift, desynchronised the same way.
# Only the portal's vortex membrane, its sparks and the traveller crossing through still need
# real frames (hero_mid_gate_frame); the portal island and its gate stonework are static too
# (hero_mid_stones), sharing one bob with the gate overlay via main.css's shared wrapper.

HERO = (1440, 640)
HERO_FAR_SPOTS = [
    # (x, y, radius, phase, amplitude)
    (160, 90, 4, 0.00, 2.0), (430, 60, 3, 0.30, 2.0), (980, 70, 4, 0.55, 2.0),
    (1260, 110, 3, 0.80, 2.0), (60, 220, 3, 0.15, 2.0), (700, 40, 3, 0.65, 2.0),
]
HERO_NEAR_SPOTS = [
    # (seed, x, y, radius, depth, trees, s, phase, amplitude)
    (401, -60, 520, 7, 3, 2, 20, 0.10, 2.5),
    (402, 1330, -40, 5, 3, 1, 17, 0.50, 2.2),
]
HERO_MID_OY = 330
HERO_MID_PHASE, HERO_MID_AMP = 0.0, 2.0
HERO_GATE_FRAMES = 48
HERO_GATE_MS = 40  # 48 x 40ms = 24fps, a 1.92s loop
# The gate overlay (membrane + sparks + traveller) is rendered on its own small canvas instead of
# the full 1440x640 hero: a Scene's cost is dominated by its canvas area (painted and blurred at
# SS*R px per page px), and the gate only ever occupies a small corner of the hero, so rendering
# it full-size and cropping after wasted most of that work. HERO_MID_GATE_SHIFT is where this
# canvas's own (0, 0) sits in the hero's page-px space (chosen with margin around the gate's
# known footprint); HERO_MID_GATE_OX/OY place the gate within it the same way HERO_MID_OY places
# it in the full hero.
HERO_MID_GATE_CANVAS = (300, 300)
HERO_MID_GATE_SHIFT = (600, 140)
HERO_MID_GATE_OX = 720 - HERO_MID_GATE_SHIFT[0]
HERO_MID_GATE_OY = HERO_MID_OY - HERO_MID_GATE_SHIFT[1]


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
        # bottom stays a legible (if deep) blue instead of near-black. Keeping more of the nebula
        # texture (0.5 instead of 0.35) than the flat tint stops the sky reading as a uniform
        # overcast gradient with no place in it.
        clouds = clouds * 0.5 + np.array(mix(SLATE_LIGHT, ICE, 0.5), dtype=float) * 0.5
        top_tint = np.array(mix(ICE, WHITE, 0.5), dtype=float)
        fade = np.clip(1 - np.arange(h) / (h * 0.6), 0, 1)[:, None, None] ** 1.4
        clouds = clouds * (1 - fade * 0.7) + top_tint * (fade * 0.7)
        # a warm patch centred where the portal island sits (roughly mid-height, mid-width):
        # daylight with nowhere warm in it read as flat; this is the same "lit overworld" cue the
        # dark variant gets from its portal glow, without needing the portal art itself here.
        yy, xx = np.mgrid[0:h, 0:w]
        portal_dist = np.sqrt(((xx - w * 0.5) / (w * 0.42)) ** 2 + ((yy - h * 0.56) / (h * 0.38)) ** 2)
        warm = np.clip(1 - portal_dist, 0, 1)[:, :, None] ** 1.8
        clouds = clouds * (1 - warm * 0.4) + np.array(SALMON_LIGHT, dtype=float) * (warm * 0.4)
    else:
        top_tint = np.array(mix(ICE, SALMON_LIGHT, 0.3), dtype=float)
        fade = np.clip(1 - np.arange(h) / (h * 0.5), 0, 1)[:, None, None] ** 1.6
        clouds = clouds * (1 - fade * 0.55) + top_tint * (fade * 0.55)
    bg = np.asarray(moddb.to_image(clouds).resize((w * R, h * R), Image.BICUBIC), dtype=float).copy()
    moddb.sprinkle_stars(rng, bg, 90 if light else 420, wrap=False)
    return moddb.to_image(bg).convert("RGBA")


def hero_far_island(n, x, y, radius):
    """One far-layer island, alone in a HERO-sized scene at its un-bobbed spot so its crop lines
    up with the hero canvas exactly; the bob is a CSS animation on the saved crop, not a frame."""
    w, h = HERO
    scene = Scene(w, h)
    rng = np.random.default_rng(300 + n)
    scene.add(island(rng, radius, 1, trees=rng.integers(0, 2)), x, y, 5)
    return scene.render(bloom=5, strength=0.5)


def hero_near_island(seed, x, y, radius, depth, trees, s):
    """One near-layer island, alone in a HERO-sized scene (see hero_far_island)."""
    w, h = HERO
    scene = Scene(w, h)
    scene.add(island(np.random.default_rng(seed), radius, depth, trees=trees), x, y, s)
    return scene.render(bloom=8, strength=0.55)


def hero_mid_stones():
    """The portal island and its gate stonework: pillars, lintel, keystone, rune inlays. Static -
    the same image every frame - so gate()'s membrane and sparks (small emissive voxels, size <
    0.5) are left out here and rendered separately by hero_mid_gate_frame, since those are the
    only part of the portal that actually needs to animate."""
    w, h = HERO
    gate_vox, cells = gate(-3, -2, 0, 4, 6, np.random.default_rng(2), t=0.0)
    base = island(np.random.default_rng(3), 6, 3, keep_clear=cells,
                  light=((0, -1), SALMON_LIGHT, 5), tree_cells=[(-4, -4), (4, -3), (4, 4)])
    stones = [v for v in gate_vox if v[5] >= 0.5]  # v = (i, j, k, color, alpha, size, emissive)
    scene = Scene(w, h)
    scene.add(base + stones, w / 2, HERO_MID_OY, 15)
    return scene.render(bloom=15, strength=0.7)


def hero_mid_gate_frame(t):
    """The vortex membrane, its sparks, and the traveller crossing through: the only part of the
    portal that moves frame to frame. Same formulas as the old hero_mid(t), just on the small
    dedicated canvas above instead of the full hero, and no longer sharing a frame budget with
    the static stonework in hero_mid_stones."""
    w, h = HERO_MID_GATE_CANVAS
    gate_vox, cells = gate(-3, -2, 0, 4, 6, np.random.default_rng(2), t=t)
    glow = [v for v in gate_vox if v[5] < 0.5]
    scene = Scene(w, h)
    scene.add(glow, HERO_MID_GATE_OX, HERO_MID_GATE_OY, 15)
    img = scene.render(bloom=15, strength=0.7)

    # the traveller walks up from the grass, through the opening, and fades into the membrane
    phase = t % 1.0
    if phase < 0.6:
        u = phase / 0.6
        j = 3.0 - 3.6 * u
        alpha = int(255 * min(1.0, u / 0.15) * min(1.0, (1 - u) / 0.35))
        fig = Scene(w, h)
        # size=0.9 rendered at roughly 7 page px, unreadable at hero scale even at the loop's
        # midpoint; 2.4 reads as a small figure instead of a flicker.
        fig.add(traveller(0.3, j, 0.05, alpha=alpha, size=2.4), HERO_MID_GATE_OX, HERO_MID_GATE_OY, 15)
        img.alpha_composite(fig.render(bloom=0))
    return img


def save_hero_crop(name, img):
    """Crop a HERO-canvas render to its own content and save it as a static PNG. Returns the crop
    box in page px (left, top, width, height) so main.css/index.md can position it as a
    percentage of the hero stage."""
    box = img.getbbox()
    img.crop(box).save(OUT / f"{name}.png", optimize=True)
    left, top, right, bottom = (v / R for v in box)
    return left, top, right - left, bottom - top


def save_hero_gate(name, frames, quality=40, frame_ms=HERO_GATE_MS, shift=(0, 0)):
    """Writes the gate overlay's looping webp (cropped to the union of every frame's content, not
    its whole own canvas), a still png (first frame) for prefers-reduced-motion, and a half-size
    -720 companion for the phone crop. Returns the crop box in page px within the hero (not the
    frames' own small canvas): shift is where that canvas's (0, 0) sits in the hero, as page px,
    same as save_hero_crop for every other hero image."""
    box = union_box(frames)
    cropped = [f.crop(box) for f in frames]
    cropped[0].save(OUT / f"{name}.webp", save_all=True, append_images=cropped[1:],
                    duration=frame_ms, loop=0, quality=quality, method=6)
    cropped[0].save(OUT / f"{name}.png", optimize=True)
    half = [f.resize((f.width // 2, f.height // 2), Image.LANCZOS) for f in cropped]
    half[0].save(OUT / f"{name}-720.webp", save_all=True, append_images=half[1:],
                 duration=frame_ms, loop=0, quality=quality, method=6)
    left, top, right, bottom = (v / R for v in box)
    return left + shift[0], top + shift[1], right - left, bottom - top


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


# ---------------------------------------------------------------- island underside

UNDERSIDE_BLOCK = 12          # page pixels per block
UNDERSIDE_ROWS = (15, 13, 10, 7, 4, 2)  # blocks per row, top to bottom


def island_underside(seed=51):
    """The hanging root of a floating island, drawn under a landing card.

    Seen from the front rather than in isometric: an isometric cone has a V-shaped top edge
    that no straight cut can line up with the straight bottom of a card, while rows of blocks
    narrowing downward sit flush with it. Earth first, then stone darkening with depth, each
    block lit from above like the renderer's cubes, with a little jitter so the rows look dug
    rather than stacked."""
    rng = np.random.default_rng(seed)
    b = UNDERSIDE_BLOCK * R
    cols = UNDERSIDE_ROWS[0]
    img = Image.new("RGBA", (cols * b, len(UNDERSIDE_ROWS) * b))
    draw = ImageDraw.Draw(img)
    for row, count in enumerate(UNDERSIDE_ROWS):
        shift = int(rng.integers(-1, 2)) if 0 < row < len(UNDERSIDE_ROWS) - 1 else 0
        start = (cols - count) // 2 + shift
        base = EARTH if row == 0 else mix(SLATE_LIGHT, SLATE, row / (len(UNDERSIDE_ROWS) - 1))
        for c in range(start, start + count):
            color = moddb.shade(base, 0.9 + 0.2 * rng.random())
            x0, y0 = c * b, row * b
            draw.rectangle([x0, y0, x0 + b - 1, y0 + b - 1], fill=color + (255,))
            draw.rectangle([x0, y0, x0 + b - 1, y0 + max(1, b // 6)], fill=moddb.shade(color, 1.18) + (255,))
            draw.rectangle([x0 + b - max(1, b // 8), y0, x0 + b - 1, y0 + b - 1], fill=moddb.shade(color, 0.78) + (255,))
    return img


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

def save_animated(name, frame_at, still_t, frames, quality=76, crop=True, size=None, xfade=0.08, mobile=False, frame_ms=None):
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
    # hero: the opaque background is one full-canvas layer, still aligned to HERO exactly so it
    # stacks under the rest without drifting. Every island is its own tightly-cropped static
    # image now (see the hero (parallax layers) section above), positioned in main.css/index.md
    # from the page-px boxes printed below; only the portal's vortex membrane, sparks and
    # traveller are still real animation, cropped to the gate instead of the full canvas.
    for light, suffix in ((False, ""), (True, "-light")):
        bg = hero_background(light=light)
        bg.convert("RGB").save(OUT / f"hero-bg{suffix}.webp", quality=82, method=6)
        bg.resize((bg.width // 2, bg.height // 2), Image.LANCZOS).convert("RGB").save(
            OUT / f"hero-bg{suffix}-720.webp", quality=82, method=6)

    print("-- hero layout: name  left top width height (page px)  phase  amp --")
    for n, (x, y, radius, phase, amp) in enumerate(HERO_FAR_SPOTS):
        box = save_hero_crop(f"hero-far-{n + 1}", hero_far_island(n, x, y, radius))
        print(f"hero-far-{n + 1}", box, phase, amp)
    for n, (seed, x, y, radius, depth, trees, s, phase, amp) in enumerate(HERO_NEAR_SPOTS):
        box = save_hero_crop(f"hero-near-{n + 1}", hero_near_island(seed, x, y, radius, depth, trees, s))
        print(f"hero-near-{n + 1}", box, phase, amp)
    box = save_hero_crop("hero-mid-island", hero_mid_stones())
    print("hero-mid-island", box, HERO_MID_PHASE, HERO_MID_AMP)
    gate_frames = [hero_mid_gate_frame(n / HERO_GATE_FRAMES) for n in range(HERO_GATE_FRAMES)]
    box = save_hero_gate("hero-mid-gate", gate_frames, shift=HERO_MID_GATE_SHIFT)
    print("hero-mid-gate", box, HERO_MID_PHASE, HERO_MID_AMP)

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
    # radius=1 (drift-2's old value) crops to a much smaller bounding box than the others at the
    # same R, so at the landing's uniform 110px display width it was upscaled and blurry; radius=2
    # across all three keeps them the same native density.
    for n, (seed, radius, trees) in enumerate(((61, 2, 1), (62, 2, 0), (63, 2, 0))):
        drift_island(seed, radius, trees).save(OUT / f"drift-{n + 1}.png", optimize=True)

    for f in sorted(OUT.glob("*.png")) + sorted(OUT.glob("*.webp")):
        img = Image.open(f)
        print(f.name, img.size, f.stat().st_size // 1024, "KB")
