# Badges

Your mod needs Manifold installed, and a player who misses that line in your description gets a
mod that does not load. A badge says it at a glance and links straight to the download.

<a href="https://mods.vintagestory.at/manifold"><img src="../assets/badges/requires-manifold-wide-void.png" srcset="../assets/badges/requires-manifold-wide-void@2x.png 2x" width="600" height="100" alt="Requires Manifold" style="max-width:100%;height:auto"></a>

The kit has five formats in three palettes. Every badge carries its own opaque ground and frame,
so it reads on any page background.

## On a Mod DB page

Open your mod's page in the editor, switch to the source view (the `<>` button) and paste this
where the badge should sit:

```html
<a href="https://mods.vintagestory.at/manifold"><img src="https://raw.githubusercontent.com/Pixnop/Manifold/main/docs/assets/badges/requires-manifold-plaque-void.png" width="220" alt="Requires Manifold" style="border:0"></a>
```

That is the plaque on the dark palette:

<img src="../assets/badges/requires-manifold-plaque-void.png" srcset="../assets/badges/requires-manifold-plaque-void@2x.png 2x" width="220" height="60" alt="Requires Manifold, plaque, void">

Keep the `width` attribute: Mod DB strips most styling, and the width is what keeps the image at
its intended size. To centre the badge, wrap the link in `<p style="text-align:center">`.

## Pick a format and a palette

Every file is named `requires-manifold-<format>-<palette>.png`. Change those two words in the
snippet and set `width` to the format's width.

| Format | void | light | slate |
| --- | --- | --- | --- |
| **plaque**<br>220x60 | <img src="../assets/badges/requires-manifold-plaque-void.png" srcset="../assets/badges/requires-manifold-plaque-void@2x.png 2x" width="200" alt="requires manifold, plaque, void"> | <img src="../assets/badges/requires-manifold-plaque-light.png" srcset="../assets/badges/requires-manifold-plaque-light@2x.png 2x" width="200" alt="requires manifold, plaque, light"> | <img src="../assets/badges/requires-manifold-plaque-slate.png" srcset="../assets/badges/requires-manifold-plaque-slate@2x.png 2x" width="200" alt="requires manifold, plaque, slate"> |
| **wide**<br>600x100 | <img src="../assets/badges/requires-manifold-wide-void.png" srcset="../assets/badges/requires-manifold-wide-void@2x.png 2x" width="200" alt="requires manifold, wide, void"> | <img src="../assets/badges/requires-manifold-wide-light.png" srcset="../assets/badges/requires-manifold-wide-light@2x.png 2x" width="200" alt="requires manifold, wide, light"> | <img src="../assets/badges/requires-manifold-wide-slate.png" srcset="../assets/badges/requires-manifold-wide-slate@2x.png 2x" width="200" alt="requires manifold, wide, slate"> |
| **square**<br>160x160 | <img src="../assets/badges/requires-manifold-square-void.png" srcset="../assets/badges/requires-manifold-square-void@2x.png 2x" width="160" alt="requires manifold, square, void"> | <img src="../assets/badges/requires-manifold-square-light.png" srcset="../assets/badges/requires-manifold-square-light@2x.png 2x" width="160" alt="requires manifold, square, light"> | <img src="../assets/badges/requires-manifold-square-slate.png" srcset="../assets/badges/requires-manifold-square-slate@2x.png 2x" width="160" alt="requires manifold, square, slate"> |
| **seal**<br>112x112 | <img src="../assets/badges/requires-manifold-seal-void.png" srcset="../assets/badges/requires-manifold-seal-void@2x.png 2x" width="112" alt="requires manifold, seal, void"> | <img src="../assets/badges/requires-manifold-seal-light.png" srcset="../assets/badges/requires-manifold-seal-light@2x.png 2x" width="112" alt="requires manifold, seal, light"> | <img src="../assets/badges/requires-manifold-seal-slate.png" srcset="../assets/badges/requires-manifold-seal-slate@2x.png 2x" width="112" alt="requires manifold, seal, slate"> |
| **flat**<br>138x20 | <img src="../assets/badges/requires-manifold-flat-void.png" srcset="../assets/badges/requires-manifold-flat-void@2x.png 2x" width="138" alt="requires manifold, flat, void"> | <img src="../assets/badges/requires-manifold-flat-light.png" srcset="../assets/badges/requires-manifold-flat-light@2x.png 2x" width="138" alt="requires manifold, flat, light"> | <img src="../assets/badges/requires-manifold-flat-slate.png" srcset="../assets/badges/requires-manifold-flat-slate@2x.png 2x" width="138" alt="requires manifold, flat, slate"> |

- **void** is the dark ground of Manifold's own page. Use it on a dark page.
- **light** is slate ink on a near-white ground, for a GitHub README on the light theme.
- **slate** is white ink on slate blue, for a page that is neither.

- **plaque** (220 px wide). A small framed rectangle. The one to reach for on a Mod DB page, next to your dependency list.
- **wide** (600 px wide). A banner with a subtitle, for the top or the bottom of a Mod DB description.
- **square** (160 px wide). A tile, for a sidebar or a gallery slot.
- **seal** (112 px wide). A round mark, for a page corner or a footer.
- **flat** (138 px wide). A strip for a README badge row, next to your CI and license badges.

## In a GitHub README

```md
[![Requires Manifold](https://raw.githubusercontent.com/Pixnop/Manifold/main/docs/assets/badges/requires-manifold-flat-void.png)](https://mods.vintagestory.at/manifold)
```

## On the Vintage Story forum

```bbcode
[url=https://mods.vintagestory.at/manifold][img]https://raw.githubusercontent.com/Pixnop/Manifold/main/docs/assets/badges/requires-manifold-plaque-void.png[/img][/url]
```

## Sharper on high-density screens

Each badge also ships at twice the size, with `@2x` before `.png`. Point `src` at that file and
keep the same `width`:

```html
<a href="https://mods.vintagestory.at/manifold"><img src="https://raw.githubusercontent.com/Pixnop/Manifold/main/docs/assets/badges/requires-manifold-plaque-void@2x.png" width="220" alt="Requires Manifold" style="border:0"></a>
```

The files and the script that draws them live in
[`docs/assets/badges`](https://github.com/Pixnop/Manifold/tree/main/docs/assets/badges).
