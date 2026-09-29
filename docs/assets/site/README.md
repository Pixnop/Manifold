# Documentation site artwork

Procedural art for the Manifold documentation site, seeded and reproducible:

    python3 docs/assets/site/generate.py

This reuses the voxel renderer built for the Mod DB page (`docs/assets/moddb/generate.py`)
instead of duplicating it: `Scene`, `vox`, `cube`, `island`, `gate`, `arcs_layer`, `nebula`, the
palette and the rest of the drawing machinery are imported from there by path (both files are
called `generate.py`, so the import gives the Mod DB module its own name, `moddb_generate`, to
avoid a collision). Only the scenes below, and a couple of new pieces (the traveller figure, the
animated divider, the closed gate for the 404), live in this file.

Needs Pillow and NumPy, same as the Mod DB generator. A full run takes several minutes, most of
it in the header and highlight loops (each rendered at full card size, one frame at a time).

Every file is written at twice the size the page displays it at (`R` in `generate.py`), so it
stays sharp on high-density screens. The sizes below are display sizes; the page sets each
`<img>` or background layer to that width.

## Hero (`docs/assets/site/hero-*`)

The hero used to be three full-canvas animated layers (far/mid/near), each 22 frames at 220ms -
a slow bob baked into raster frames, which came out to only 4.5 frames per second and read as
choppy. It is now one opaque background plus a set of small, individually-cropped island images
that sit inside the same 1440x640 box (a stage `<div>` in `index.md`, see `mf-hero__stage` /
`mf-hero__island` / `mf-hero__bob` in `main.css`): the bob is a CSS keyframe (a pure small
translation, so there is no reason to spend frames on it), applied to a wrapper *inside* each
depth layer rather than the layer itself, so it never fights the pointer/scroll parallax
transform main.js writes on the layer. Only the portal's vortex membrane, its sparks, and the
traveller crossing through are still real animation, since those actually change shape frame to
frame; everything else is a still image that moves by CSS alone.

| File | Display size | Loop | Use |
| --- | --- | --- | --- |
| `hero-bg.webp` / `-light.webp` | 1440x640 | - | Opaque nebula and stars (dark/light theme), lighter near the top fading to void (or sky) at the bottom. No animation. |
| `hero-far-1..6.png` | varies, cropped to content | - | The six small, distant islands, each its own static transparent crop. Bobs via CSS (`--mf-bob`, `animation-delay` set per island from its old phase). |
| `hero-near-1..2.png` | varies, cropped to content | - | The one or two large islands cropped by the hero's frame edge, closer to the viewer. Same static-crop-plus-CSS-bob treatment as `hero-far-*`. |
| `hero-mid-island.png` | 273x300 | - | The portal island and its gate stonework, with a hole where `hero-mid-gate.webp` draws the moving part. |
| `hero-mid-gate.webp` / `.png` | 226x191 | 48 frames, 1.92s (25fps) | Everything around the portal that moves: the vortex membrane, its sparks and the traveller. Each frame is the whole portal island rendered depth-sorted, cut to the region where any frame differs from the still, and that region is cut out of `hero-mid-island.png`, so the pillar and the trees in front stay in front and nothing is drawn twice. Shares its bob wrapper with `hero-mid-island.png`. `.png` is the reduced-motion still. |

`hero-far-*` and `hero-near-*` need no `-720` companion: each is already a few KB, cropped to its
own content by `save_hero_crop` in `generate.py`. `hero-mid-gate.webp` is the one hero asset
still worth halving for phones (`hero-mid-gate-720.webp`), since its swirling, ever-changing
membrane pattern does not compress as well as the mostly-static islands. Rendering the gate
overlay on its own small canvas (`HERO_MID_GATE_CANVAS`) instead of the full 1440x640 hero, then
cropping, is also what keeps `generate.py` fast: a `Scene`'s cost scales with its canvas area,
and the gate only ever occupies a small corner of it.

Combined weight for a motion-enabled visit at 1440 (`hero-bg` + `hero-bg-light` + every
`hero-far`/`hero-near`/`hero-mid-island` crop + `hero-mid-gate.webp`) is about 770 KB, down from
the old four-layer set's roughly 1.05 MB. At the phone crop (390 width, `-720`/half-size
companions where they exist) it is about 490 KB, down from roughly 520 KB.

## 0.6 highlight loops (`docs/assets/site/highlight-*`)

Small looping cards, 220x160, one per 0.6 feature:

| File | Shows |
| --- | --- |
| `highlight-pregeneration.webp` / `.png` | A dimension's terrain rising into place with nobody there (`IManifoldServer.GenerateRegion`: no player, no transit). |
| `highlight-column-generated.webp` / `.png` | A marker rising and settling onto a freshly generated column (`IDimensionRegistry.ColumnGenerated`). |
| `highlight-client-metadata.webp` / `.png` | A tagged packet travelling along an arc from a server island to a client island. |
| `highlight-safe-landing.webp` / `.png` | A figure dropping onto dry ground beside a pond, not into it (the two-block clearance / no-landing-in-liquid search). |
| `highlight-schema-saves.webp` / `.png` | A save chest that stays intact while its version tag glows (the `SchemaSidecar` record kept alongside each persisted blob). |

## Article headers (`docs/assets/site/header-*`)

Same 220x160 card format, one per docfx article, named after the article file:

| File | Article | Shows |
| --- | --- | --- |
| `header-getting-started.webp` / `.png` | `getting-started.md` | A portal gate assembling stone by stone, then lighting up, then fading for the next loop. |
| `header-dimensions.webp` / `.png` | `dimensions.md` | Two settled islands and a third, ephemeral one fading in and out. |
| `header-worldgen.webp` / `.png` | `worldgen.md` | The Mod DB chunk-building loop, reused as-is (`moddb.worldgen_scene`). |
| `header-transit-and-travel-policy.webp` / `.png` | `transit-and-travel-policy.md` | The Mod DB chest-through-portal loop, reused as-is (`moddb.transit_scene`). |
| `header-architecture.webp` / `.png` | `architecture.md` | Three islands linked by transit arcs, with a pulse riding each one. |

## Divider and 404

| File | Display size | Use |
| --- | --- | --- |
| `divider.webp` / `.png` | 800x28 | The Mod DB divider line, with a single spark travelling its length; fades in and out at the ends so the loop has no visible seam. |
| `not-found.webp` / `.png` | 320x233 (`NOT_FOUND_SIZE`, not the card format) | A lone figure drifting near a closed gate (dimmed stonework, still legible; no sparks, no keystone glow), for the 404 page, which draws it full-bleed and scaled up further in CSS. |

## Island underside (`docs/assets/site/island-underside.png`)

One full-width band per floating-island card (`.mf-card--island` in `main.css`), not a repeat-x
tile: a grass lip over dirt and stone that tapers shallower at both ends and deepest in the
middle, so it reads as the hanging root of one island. `UNDERSIDE_SIZE` in `generate.py` is the
image's exact canvas (no bbox crop), so `main.css`'s `aspect-ratio` on the `::after` that draws it
always matches.

## Mod page icons (`docs/assets/site/mod-icons/`)

The "Mods built on Manifold" strip's six icons, downloaded once from their Mod DB pages and
resized to 88x88 (2x for a 44px display size) instead of hotlinked from `moddbcdn.vintagestory.at`
at their original (up to 480x480) size - not generated, so `generate.py` doesn't touch them.

## File weight

Every card loop (`highlight-*`, `header-*`) is under 270 KB; most are well under. The divider and
404 loops are under 90 KB. The hero layers are covered above. Where a scene needed a smaller
quality/frame budget than the Mod DB defaults to stay inside those limits, that is set per call in
`save_animated(...)` at the bottom of `generate.py`, not by editing the shared renderer.

`card_sized(...)` (the shared crop-and-centre for every header/highlight loop) scales oversized
content down to fit its target canvas instead of letting it overflow past the edges, and every
frame it produces is checked by `assert_edges_clear(...)`: row/column 0 and the last row/column
must be fully transparent, so a scene that silently grows into its own frame border fails loudly
instead of shipping a clipped image.
