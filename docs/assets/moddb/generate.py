"""Generate the Mod DB page artwork for Manifold.

Everything is procedural and seeded, so a rerun reproduces the same files:

    python3 docs/assets/moddb/generate.py

Needs Pillow and NumPy. The palette is taken from docfx/images/logo.svg. Voxel scenes are
drawn at three times their final size and downsampled, which is what keeps cube edges clean
(Pillow draws polygons without anti-aliasing).
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent
SS = 3  # supersampling factor for voxel scenes

VOID = (21, 22, 24)
DEEP = (14, 17, 22)
SLATE = (51, 63, 72)
STONE = (63, 71, 78)
SLATE_MID = (75, 96, 108)
SLATE_LIGHT = (92, 123, 137)
SALMON = (226, 143, 112)
SALMON_LIGHT = (226, 161, 145)
GREEN = (140, 178, 116)
ICE = (213, 237, 240)
EARTH = (122, 86, 66)
WHITE = (246, 250, 250)


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def shade(color, factor):
    return tuple(max(0, min(255, int(c * factor))) for c in color)


# ---------------------------------------------------------------- void background

def periodic_noise(rng, size, falloff):
    """Seamless noise: white noise shaped in the frequency domain wraps on both axes."""
    white = rng.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1.0
    field = np.real(np.fft.ifft2(np.fft.fft2(white) / radius ** falloff))
    field -= field.min()
    return field / field.max()


def nebula(rng, width, height, tile):
    """Dark ground with faint salmon and slate clouds; seamless when tile is True."""
    size = max(width, height)
    fields = [periodic_noise(rng, size, f) for f in (1.6, 2.2, 2.2)]
    if tile:
        base, warm, cold = (f[:height, :width] for f in fields)
    else:
        base, warm, cold = (np.array(Image.fromarray((f * 255).astype(np.uint8)).resize((width, height),
                                                                                      Image.BICUBIC)) / 255.0
                            for f in fields)
    img = np.zeros((height, width, 3))
    for c in range(3):
        img[..., c] = DEEP[c] + (VOID[c] + 10 - DEEP[c]) * base
    warm_mask = np.clip((warm - 0.62) * 3.0, 0, 1)[..., None] * 0.14
    cold_mask = np.clip((cold - 0.60) * 3.0, 0, 1)[..., None] * 0.13
    img = img * (1 - warm_mask) + np.array(SALMON) * warm_mask * 0.5 + img * warm_mask * 0.5
    img = img * (1 - cold_mask) + np.array(SLATE_LIGHT) * cold_mask
    return img


def sprinkle_stars(rng, img, count, wrap):
    height, width, _ = img.shape

    def put(px, py, color, a):
        if wrap:
            px, py = px % width, py % height
        elif not (0 <= px < width and 0 <= py < height):
            return
        img[py, px] = img[py, px] * (1 - a) + color * a

    for _ in range(count):
        x, y = int(rng.integers(0, width)), int(rng.integers(0, height))
        bright = rng.random() ** 3
        color = np.array(ICE if rng.random() < 0.82 else SALMON_LIGHT, dtype=float)
        put(x, y, color, 0.30 + 0.70 * bright)
        if bright > 0.45:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                put(x + dx, y + dy, color, 0.30 * bright)
        if bright > 0.85:  # the rare bright one gets a small cross sparkle
            for d in (2, 3):
                for dx, dy in ((d, 0), (-d, 0), (0, d), (0, -d)):
                    put(x + dx, y + dy, color, 0.18 * bright / (d - 1))


def to_image(arr):
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")


# ---------------------------------------------------------------- isometric voxel scenes
#
# A voxel is (i, j, k, color, alpha, size, emissive). Screen: x = ox + (i - j) * s,
# y = oy + (i + j) * s / 2 - k * s. Larger i + j is closer to the viewer. A voxel's top face
# sits at height k and the cube hangs down by one voxel (size scales it).

def vox(i, j, k, color, alpha=255, size=1.0, emissive=False):
    return (i, j, k, color, alpha, size, emissive)


def cube(draw, x, y, s, color, alpha, emissive):
    top = [(x, y - s / 2), (x + s, y), (x, y + s / 2), (x - s, y)]
    left = [(x - s, y), (x, y + s / 2), (x, y + s / 2 + s), (x - s, y + s)]
    right = [(x + s, y), (x, y + s / 2), (x, y + s / 2 + s), (x + s, y + s)]
    lf, rf, tf = (0.94, 0.84, 1.06) if emissive else (0.72, 0.52, 1.0)
    draw.polygon(left, fill=shade(color, lf) + (alpha,))
    draw.polygon(right, fill=shade(color, rf) + (alpha,))
    draw.polygon(top, fill=shade(color, tf) + (alpha,))
    if not emissive and s >= 6:
        draw.line([top[3], top[0], top[1]], fill=shade(color, 1.18) + (min(alpha, 140),), width=max(1, SS // 2))


class Scene:
    """Collects voxels and renders them supersampled, with a bloom pass for emissive ones."""

    def __init__(self, width, height):
        self.width, self.height = width, height
        self.items = []

    def add(self, voxels, ox, oy, s):
        self.items += [(v, ox, oy, s) for v in voxels]

    def _paint(self, only_emissive):
        layer = Image.new("RGBA", (self.width * SS, self.height * SS))
        draw = ImageDraw.Draw(layer)
        order = sorted(self.items, key=lambda it: (it[0][0] + it[0][1], it[0][2], it[0][5]))
        for (i, j, k, color, alpha, size, emissive), ox, oy, s in order:
            if (only_emissive and not emissive) or alpha < 6:
                continue
            x = (ox + (i - j) * s) * SS
            y = (oy + (i + j) * s / 2 - k * s) * SS
            half = s * size * SS
            if alpha >= 255:
                cube(draw, x, y, half, color, alpha, emissive)
                continue
            # ImageDraw overwrites the destination alpha, so a translucent cube would punch a hole
            # through what is behind it: draw it on its own patch and composite it properly.
            x0, y0 = int(x - half) - 1, int(y - half / 2) - 1
            patch = Image.new("RGBA", (int(2 * half) + 3, int(2 * half) + 3))
            cube(ImageDraw.Draw(patch), x - x0, y - y0, half, color, alpha, emissive)
            if x0 >= 0 and y0 >= 0:
                layer.alpha_composite(patch, (x0, y0))
            else:
                layer.alpha_composite(patch.crop((max(0, -x0), max(0, -y0), patch.width, patch.height)),
                                      (max(0, x0), max(0, y0)))
        return layer

    def render(self, bloom=14, strength=1.0):
        """Returns the scene with its emissive voxels' light added on top (additive bloom)."""
        scene = self._paint(False).resize((self.width, self.height), Image.LANCZOS)
        if not bloom:
            return scene
        glow_src = np.asarray(self._paint(True).resize((self.width, self.height), Image.LANCZOS), dtype=float)
        light = np.zeros((self.height, self.width, 3))
        for radius, weight in ((bloom, 1.0), (bloom / 3, 0.7), (bloom * 2.2, 0.45)):
            blurred = np.asarray(Image.fromarray(glow_src.astype(np.uint8), "RGBA").filter(
                ImageFilter.GaussianBlur(radius)), dtype=float)
            light += blurred[..., :3] * (blurred[..., 3:] / 255.0) * weight
        light *= strength
        base = np.asarray(scene, dtype=float)
        # light falls mostly around the emitters: an opaque pixel only takes a quarter of it,
        # so the membrane keeps its pattern instead of burning out to white
        rgb = base[..., :3] * (base[..., 3:] / 255.0) + light * (1 - 0.75 * base[..., 3:] / 255.0)
        alpha = np.clip(np.maximum(base[..., 3], light.max(axis=2) * 1.1), 0, 255)
        # un-premultiply so the RGBA composites correctly over any background
        safe = np.maximum(alpha, 1)[..., None] / 255.0
        out = np.dstack([np.clip(rgb / safe, 0, 255), alpha])
        return Image.fromarray(out.astype(np.uint8), "RGBA")


def island(rng, radius, depth, trees=0, keep_clear=None, light=None, tree_cells=None):
    """Rolling grass on top, a stone cone hanging underneath, optional trees.

    keep_clear: set of (i, j) cells that must stay flat and treeless (e.g. under a gate).
    light: ((ci, cj), color, reach) tints the grass near a light source.
    """
    keep_clear = keep_clear or set()
    voxels, tops = [], {}
    for i in range(-radius, radius + 1):
        for j in range(-radius, radius + 1):
            d = (i * i + j * j) ** 0.5
            if d > radius + 0.3:
                continue
            hill = 1 if (d < radius * 0.6 and rng.random() < 0.5 and (i, j) not in keep_clear) else 0
            under = 1 + int(round((1 - d / (radius + 0.5)) ** 1.3 * depth * 2 + rng.random() * 1.6))
            grass = GREEN
            if light:
                (ci, cj), lcolor, reach = light
                near = max(0.0, 1 - (((i - ci) ** 2 + (j - cj) ** 2) ** 0.5) / reach)
                grass = mix(GREEN, lcolor, 0.38 * near)
            for k in range(-under, hill + 1):
                if k == hill:
                    color = grass
                elif k >= hill - 1:
                    color = EARTH
                else:
                    color = mix(SLATE_LIGHT, SLATE, -k / (depth * 2 + 1))
                voxels.append(vox(i, j, k, color))
            tops[(i, j)] = hill
    spots = [p for p in tops if (p[0] ** 2 + p[1] ** 2) ** 0.5 < radius - 0.6
             and all((p[0] + a, p[1] + b) not in keep_clear for a in (-1, 0, 1) for b in (-1, 0, 1))]
    chosen = list(tree_cells) if tree_cells else [spots.pop(int(rng.integers(0, len(spots))))
                                                  for _ in range(min(trees, len(spots)))]
    for i, j in chosen:
        base = tops[(i, j)]
        leaf = mix(GREEN, SLATE_MID, 0.22)
        voxels += [vox(i, j, base + 1, EARTH), vox(i, j, base + 2, EARTH)]
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                if abs(a) + abs(b) < 2:
                    voxels.append(vox(i + a, j + b, base + 3, leaf))
        voxels.append(vox(i, j, base + 4, mix(GREEN, SLATE_MID, 0.1)))
    return voxels


def gate(i0, j0, base, width, height, rng, cells_per_voxel=3, t=0.0):
    """A stone portal gate standing along the i axis, with a vortex membrane between the pillars.

    Pillars at i0 and i0 + width + 1, lintel above, a step in front. Returns the voxels and the
    (i, j) cells the gate occupies, so the island under it can stay flat.
    """
    voxels, cells = [], set()
    left, right = i0, i0 + width + 1
    for k in range(base + 1, base + height + 1):
        for i in (left, right):
            rune = k in ((base + 2, base + height - 1) if height >= 6 else (base + 2,))
            # stone next to the membrane catches its light; runes are inlays, a notch darker than the vortex
            lit = mix(STONE if (k + i) % 2 else SLATE, SALMON_LIGHT, 0.18)
            pulse = 0.5 + 0.5 * np.sin(2 * np.pi * t)
            color = mix((190, 102, 80), SALMON_LIGHT, 0.35 * pulse) if rune else lit
            voxels.append(vox(i, j0, k, color, emissive=rune))
            cells.add((i, j0))
    for i in range(left - 1, right + 2):  # lintel with an overhang, and a keystone rune
        voxels.append(vox(i, j0, base + height + 1, SLATE if i in (left - 1, right + 1) else STONE))
        cells.add((i, j0))
    voxels.append(vox((left + right) / 2, j0, base + height + 2, SALMON, size=0.5, emissive=True))
    for i in range(left, right + 1):  # a low step in front of the opening
        voxels.append(vox(i, j0 + 1, base + 0.5, STONE, size=1.0))
    for i in range(left - 3, right + 4):  # keep the ground around the gate clear of trees
        for j in range(j0 - 1, j0 + 4):
            cells.add((i, j))

    # the membrane: a wall of small emissive voxels spiralling from salmon at the rim to a white core
    n_i, n_k = width * cells_per_voxel, height * cells_per_voxel
    for a in range(n_i):
        for b in range(n_k):
            u = (a + 0.5) / n_i - 0.5
            v = (b + 0.5) / n_k - 0.5
            r = ((u / 0.5) ** 2 + (v / 0.5) ** 2) ** 0.5
            theta = np.arctan2(v, u)
            # three arms winding inward; one loop advances the phase by a full turn, so it tiles in time
            arms = (0.5 + 0.5 * np.sin(theta * 3 - r * 12 + 2 * np.pi * t)) ** 2
            deep = mix((168, 84, 70), SALMON, r)
            color = mix(deep, ICE, arms * (1.05 - r * 0.5))
            color = mix(color, WHITE, max(0.0, 0.26 - r) * 3.0)
            alpha = 250
            i = left + 0.5 + (a + 0.5) / cells_per_voxel
            k = base + (b + 1) / cells_per_voxel
            voxels.append(vox(i, j0, k, color, alpha=alpha, size=1 / cells_per_voxel, emissive=True))

    # sparks: tiny cubes leaving the opening, rising and drifting toward the viewer, each on its
    # own cycle; they fade in and out so the loop has no visible seam
    for _ in range(3 * height):
        i = left + 0.5 + rng.random() * width
        start, lift, drift = rng.random(), rng.random(), rng.random()
        color = ICE if rng.random() < 0.55 else SALMON_LIGHT
        peak, size = 150 + rng.random() * 105, 0.14 + rng.random() * 0.1
        phase = (start + t) % 1.0
        k = base + height * (0.3 + 0.35 * lift + 0.55 * phase)
        j = j0 + 0.9 + drift * 0.8 + phase * 1.4
        voxels.append(vox(i, j, k, color, alpha=int(peak * np.sin(np.pi * phase)), size=size, emissive=True))
    return voxels, cells


def bezier(p0, p1, bend, u):
    (x0, y0), (x1, y1) = p0, p1
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2 - bend
    return ((1 - u) ** 2 * x0 + 2 * (1 - u) * u * mx + u * u * x1,
            (1 - u) ** 2 * y0 + 2 * (1 - u) * u * my + u * u * y1)


def dotted_arc(draw, p0, p1, bend, color, dash, gap, width, offset=0.0):
    """Dashes along a quadratic curve; increasing offset slides them from p0 toward p1."""
    pts = [bezier(p0, p1, bend, n / 240) for n in range(241)]
    period = dash + gap
    phase = (-offset) % period
    on, run = (True, phase) if phase < dash else (False, phase - dash)
    for a, b in zip(pts, pts[1:]):
        if on:
            draw.line([a, b], fill=color, width=width)
        run += ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2) ** 0.5
        if run >= (dash if on else gap):
            run, on = 0.0, not on


def arcs_layer(width, height, arcs, t=0.0, travellers=0):
    """Transit arcs. Dashes advance two full periods per loop; travellers ride each arc to its end."""
    layer = Image.new("RGBA", (width * SS, height * SS))
    d = ImageDraw.Draw(layer)
    dash, gap = 6 * SS, 7 * SS
    for p0, p1, bend, color in arcs:
        a, b = (p0[0] * SS, p0[1] * SS), (p1[0] * SS, p1[1] * SS)
        dotted_arc(d, a, b, bend * SS, color, dash, gap, 2 * SS, offset=2 * (dash + gap) * t)
    layer = layer.resize((width, height), Image.LANCZOS)
    if travellers:
        dots = Image.new("RGBA", (width * SS, height * SS))
        dd = ImageDraw.Draw(dots)
        for n, (p0, p1, bend, color) in enumerate(arcs):
            for m in range(travellers):
                u = (t + m / travellers + n * 0.37) % 1.0
                x, y = bezier(p0, p1, bend, u)
                alpha = int(235 * np.sin(np.pi * u))
                r = 2.2 * SS
                dd.rectangle([x * SS - r, y * SS - r, x * SS + r, y * SS + r], fill=color[:3] + (alpha,))
        dots = dots.resize((width, height), Image.LANCZOS)
        halo = dots.filter(ImageFilter.GaussianBlur(4))
        layer.alpha_composite(halo)
        layer.alpha_composite(halo)
        layer.alpha_composite(dots)
    return layer


# ---------------------------------------------------------------- assets

FRAMES = 36          # frames per animated loop
FRAME_MS = 70        # 36 x 70 ms: a 2.5 s loop


def void_tile():
    rng = np.random.default_rng(7)
    img = nebula(rng, 512, 512, tile=True)
    sprinkle_stars(rng, img, 180, wrap=True)
    to_image(img).save(OUT / "void-tile.png", optimize=True)


def banner_background():
    w, h = 1200, 360
    rng = np.random.default_rng(11)
    bg = nebula(rng, w, h, tile=False)
    sprinkle_stars(rng, bg, 260, wrap=False)
    fade = np.clip(1 - np.linspace(0, 1.4, w), 0.45, 1)[None, :, None]  # darker left for the title
    return to_image(bg * (1 - 0.45 * fade)).convert("RGBA")


def bob(t, phase, amplitude):
    return amplitude * np.sin(2 * np.pi * (t + phase))


def banner(t, background):
    w, h = background.size
    scene = Scene(w, h)
    # the hero island carries the gate; its grass takes the portal's light
    gate_vox, cells = gate(-3, -2, 0, 4, 6, np.random.default_rng(2), t=t)
    hero = island(np.random.default_rng(3), 5, 3, keep_clear=cells, light=((0, -1), SALMON_LIGHT, 5),
                  tree_cells=[(-3, -4), (3, -4)])
    scene.add(hero + gate_vox, 900, 196 + bob(t, 0.0, 1.2), 13)
    # the other dimensions: smaller islands at different depths, each drifting on its own beat
    scene.add(island(np.random.default_rng(5), 2, 2, trees=1), 612, 96 + bob(t, 0.25, 2.5), 8)
    scene.add(island(np.random.default_rng(9), 2, 2), 1120, 70 + bob(t, 0.6, 2.0), 6)
    scene.add(island(np.random.default_rng(13), 3, 2, trees=1), 640, 258 + bob(t, 0.45, 2.2), 7)
    scene.add(island(np.random.default_rng(17), 1, 1), 150, 300 + bob(t, 0.8, 1.5), 5)
    scene.add(island(np.random.default_rng(19), 1, 1), 440, 40 + bob(t, 0.1, 1.2), 4)
    art = scene.render(bloom=14, strength=0.6)

    frame = background.copy()
    frame.alpha_composite(arcs_layer(w, h, [
        ((612, 78), (846, 128), 58, SALMON_LIGHT + (150,)),
        ((1112, 60), (930, 118), 44, ICE + (140,)),
        ((648, 236), (842, 196), 30, ICE + (130,)),
    ], t=t, travellers=2))
    frame.alpha_composite(art)
    return frame.convert("RGB")


def divider():
    w, h = 800, 28
    line = Image.new("RGBA", (w, h))
    px = line.load()
    for x in range(w):
        t = 1 - abs(x - w / 2) / (w / 2)
        color = mix(SALMON, ICE, x / w)
        for y, a in ((12, 0.45), (13, 1.0), (14, 1.0), (15, 0.45)):
            px[x, y] = color + (int(255 * a * t ** 1.2),)
    scene = Scene(w, h)
    scene.add([vox(0, 0, 0, ICE, emissive=True)], w / 2, 9, 6)
    gem = scene.render(bloom=4)
    out = line.filter(ImageFilter.GaussianBlur(3))
    out.alpha_composite(line)
    out.alpha_composite(gem)
    out.save(OUT / "divider.png", optimize=True)


def framed(img, arcs=(), t=0.0):
    """Places a vignette's drawing on the fixed 220 x 160 card canvas.

    The crop box is taken from a reference frame so an animation does not jitter.
    """
    if arcs:
        under = arcs_layer(img.width, img.height, arcs, t=t)
        under.alpha_composite(img)
        img = under
    return img


def card(art, box):
    left, top, right, bottom = box
    art = art.crop((max(0, left - 6), max(0, top - 6), min(art.width, right + 6), min(art.height, bottom + 6)))
    out = Image.new("RGBA", (220, 160))
    out.alpha_composite(art, ((220 - art.width) // 2, (160 - art.height) // 2))
    return out


def feature_dimensions():
    scene = Scene(260, 220)
    scene.add(island(np.random.default_rng(21), 2, 1, trees=1), 70, 70, 7)
    scene.add(island(np.random.default_rng(22), 2, 1, trees=1), 190, 60, 7)
    scene.add(island(np.random.default_rng(23), 3, 2, trees=1), 130, 128, 8)
    img = framed(scene.render(bloom=9), arcs=[((74, 58), (126, 110), 20, ICE + (150,)),
                                             ((186, 48), (136, 110), 20, SALMON_LIGHT + (150,))])
    card(img, img.getbbox()).save(OUT / "feature-dimensions.png", optimize=True)


def worldgen_scene(t):
    """A 4 x 4 chunk building itself: columns fill one after another, their blocks dropping in.

    0 to 0.72 of the loop builds the 16 columns, the finished chunk holds, then fades back to
    its bedrock layer so the loop can start again without a jump.
    """
    scene = Scene(260, 220)
    order = [(i, j) for s_ in range(7) for i in range(4) for j in range(4) if i + j == s_]  # back to front
    voxels = []
    fade = 1.0 if t < 0.86 else max(0.0, 1 - (t - 0.86) / 0.12)
    for idx, (i, j) in enumerate(order):
        voxels.append(vox(i, j, 0, SLATE_MID))  # bedrock layer is always there
        begin = idx / len(order) * 0.72
        for k in range(1, 4):
            color = GREEN if k == 3 else (EARTH if k == 2 else SLATE_MID)
            land = begin + (k - 1) * 0.022
            if t < land - 0.06:
                continue  # not generated yet
            drop = max(0.0, (land - t) / 0.06)  # 1 while falling in, 0 once landed
            if drop > 0:
                voxels.append(vox(i, j, k + drop * 2.4, mix(color, ICE, 0.12), alpha=int(255 * (1 - drop * 0.45))))
            elif fade > 0:
                voxels.append(vox(i, j, k, color, alpha=int(255 * fade)))
    scene.add(voxels, 130, 104, 12)
    return scene.render(bloom=7)


def transit_scene(t):
    scene = Scene(260, 220)
    gate_vox, cells = gate(-1, -1, 0, 2, 4, np.random.default_rng(31), cells_per_voxel=5, t=t)
    base = island(np.random.default_rng(32), 2, 1, keep_clear=cells | {(1, 1), (1, 2)}, light=((0, 0), SALMON_LIGHT, 3))
    # a chest slides up to the opening and dissolves into it, then fades back in at its start
    oy = 128 + bob(t, 0.0, 1.0)
    scene.add(base + gate_vox, 130, oy, 12)
    img = scene.render(bloom=9)
    u = t / 0.8 if t < 0.8 else None
    if u is not None:
        j = 2.2 - 2.9 * u
        climb = 0.5 * min(1.0, max(0.0, (1.0 - j) / 0.5))  # eases up onto the step as it reaches it
        alpha = int(255 * min(1.0, u / 0.12) * min(1.0, (1 - u) / 0.25))
        hop = 0.10 * abs(np.sin(np.pi * u * 3))
        k = 0.85 + climb + hop
        # the chest is in front of everything it passes, so it gets its own pass on top: the
        # scene's depth sort is only approximate between voxels of different sizes
        chest = Scene(scene.width, scene.height)
        chest.add([vox(0.5, j, k, EARTH, alpha=alpha, size=0.8),
                   vox(0.5, j, k + 0.08, mix(EARTH, (60, 42, 32), 0.5), alpha=alpha, size=0.82)], 130, oy, 12)
        img.alpha_composite(chest.render(bloom=0))
    return img


def feature_safety():
    scene = Scene(260, 220)
    scene.add(island(np.random.default_rng(41), 3, 2, trees=1) + [vox(1, 1, 1, SALMON)], 130, 112, 9)
    img = scene.render(bloom=9)
    rings = Image.new("RGBA", (260 * SS, 220 * SS))
    d = ImageDraw.Draw(rings)
    for n in range(3):
        m = (n * 7) * SS
        d.ellipse([(40 * SS) + m, (26 * SS) + m, (220 * SS) - m, (196 * SS) - m],
                  outline=ICE + (150 - n * 45,), width=2 * SS)
    rings = rings.resize((260, 220), Image.LANCZOS)
    rings.alpha_composite(img)
    left, top, right, bottom = rings.getbbox()
    art = rings.crop((left - 6, top - 6, right + 6, bottom + 6))
    out = Image.new("RGBA", (220, 160))
    scale = min(1.0, 150 / art.height, 210 / art.width)
    art = art.resize((int(art.width * scale), int(art.height * scale)), Image.LANCZOS)
    out.alpha_composite(art, ((220 - art.width) // 2, (160 - art.height) // 2))
    out.save(OUT / "feature-safety.png", optimize=True)


def animated(name, frame_at, still_t, box_from=None, frames=FRAMES):
    """Writes name.png (the still, at still_t) and name.webp (the full loop)."""
    count = frames
    frames = [frame_at(n / count) for n in range(count)]
    if box_from is not None:
        box = box_from(frames)
        frames = [card(f, box) for f in frames]
    still = frame_at(still_t)
    if box_from is not None:
        still = card(still, box)
    still.save(OUT / f"{name}.png", optimize=True)
    frames[0].save(OUT / f"{name}.webp", save_all=True, append_images=frames[1:], duration=FRAME_MS,
                   loop=0, quality=82, method=6)


def union_box(frames):
    boxes = [f.getbbox() for f in frames if f.getbbox()]
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


if __name__ == "__main__":
    void_tile()
    divider()
    feature_dimensions()
    feature_safety()
    background = banner_background()
    animated("banner-manifold", lambda t: banner(t, background), 0.0)
    animated("feature-worldgen", worldgen_scene, 0.8, box_from=union_box, frames=54)
    animated("feature-transit", transit_scene, 0.3, box_from=union_box, frames=48)
    for f in sorted(OUT.glob("*.png")) + sorted(OUT.glob("*.webp")):
        print(f.name, Image.open(f).size, f.stat().st_size // 1024, "KB")
