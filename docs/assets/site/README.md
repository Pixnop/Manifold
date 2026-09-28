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
it in the four hero layers.

Every file is written at twice the size the page displays it at (`R` in `generate.py`), so it
stays sharp on high-density screens. The sizes below are display sizes; the page sets each
`<img>` or background layer to that width.

## Hero (`docs/assets/site/hero-*`)

Four layers at 1440x640, meant to sit in the same absolutely-positioned box and move at different
speeds under scroll (parallax). They share that canvas size exactly, so no per-layer offset is
needed to line them up.

| File | Display size | Loop | Use |
| --- | --- | --- | --- |
| `hero-bg.png` | 1440x640 | - | Opaque nebula and stars, lighter near the top (the lit overworld) fading to void at the bottom. No animation: this is the page background colour, effectively. |
| `hero-far.webp` / `.png` | 1440x640 | 22 frames, 4.84s | Small, distant islands drifting on their own slow bob. Transparent. |
| `hero-mid.webp` / `.png` | 1440x640 | 22 frames, 4.84s | The portal island: gate, spinning vortex membrane, drifting sparks, and a traveller who climbs up from the grass and fades into the opening partway through the loop. Transparent. |
| `hero-near.webp` / `.png` | 1440x640 | 22 frames, 4.84s | One or two larger islands, each cropped by the frame edge, drifting a little closer to the viewer than the mid layer. Transparent. |

The three animated layers share the same 22 frames as the Mod DB banner, so they stay in phase
with each other over repeated loops, but play back at 220ms/frame (`HERO_MS` in `generate.py`)
instead of the Mod DB banner's 70ms: at 70ms the loop read as a jitter rather than a slow drift,
and hero-mid's traveller appeared and vanished within 1.5s. `.png` stills (one frame each) are for
`prefers-reduced-motion`.

Combined weight for a motion-enabled visit: `hero-bg.png` + the three `.webp` files, about
1.4 MB. The reduced-motion set (`hero-bg.png` + the three `.png` stills) is about 630 KB.

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
