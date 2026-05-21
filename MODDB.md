# Mod DB submission reference

Content for the Vintage Story Mod DB "Add new Mod" form, for both Manifold and its
demo. Descriptions are provided as HTML - paste them into the TinyMCE **source/code
view** (the `<>` button in the toolbar) so the formatting is kept without redoing it
by hand.

---

# Page 1 - Manifold (the library)

| Field | Value |
|---|---|
| Name | `Manifold` |
| URL Alias | `manifold` |
| Side | `Universal` |
| Status | `Draft` first, then `Published` |
| Category | `Library` |
| Tags | `Library` (primary), `Worldgen`, `teleport`, `Travel & Exploration` - NOT `Harmony Patching` |

**Summary** (max 100 chars - 72):

```
API library for creating and managing custom dimensions in Vintage Story.
```

**Links**

| Field | Value |
|---|---|
| Homepage or Forum Post Url | `https://pixnop.github.io/Manifold/` |
| Source Code Url | `https://github.com/Pixnop/Manifold` |
| Issue tracker Url | `https://github.com/Pixnop/Manifold/issues` |
| Wiki Url | `https://pixnop.github.io/Manifold/` |

**Logo**: upload `manifold-logo-480.png` (480x480) under Screenshots, then select it as the ModDB Logo.

**Release file**: `Manifold-0.1.0.zip` from https://github.com/Pixnop/Manifold/releases, targeting VS 1.21.0.

### Description (HTML - paste in TinyMCE source view)

```html
<p><strong>Manifold</strong> is a library mod for <strong>Vintage Story 1.21+</strong> that lets other mods create and manage their own <strong>custom dimensions</strong>. It adds no content of its own - install it only because another mod depends on it.</p>
<hr>
<h2>For players</h2>
<p>Manifold is a <strong>dependency</strong>. If a mod you use requires Manifold, install this alongside it. It is safe on both client and server (<strong>Universal</strong>). On its own, it does nothing visible.</p>
<hr>
<h2>For mod developers</h2>
<p>Manifold exposes a clean public API to declare and run custom dimensions:</p>
<ul>
  <li><strong>Custom dimensions</strong> - persistent or ephemeral, registered at boot or at runtime, identified by an <code>AssetLocation</code> code.</li>
  <li><strong>Active worldgen</strong> - implement <code>IWorldgenStrategy</code>; Manifold pre-generates a chunk region around the arrival point <em>before</em> the player enters (bounded mode, configurable radius). Dimensions can also opt into <strong>streaming</strong> generation via <code>.Streaming(loadRadius)</code>, which generates chunks on demand as players move with no fixed edge.</li>
  <li><strong>Player transit</strong> - one call, <code>TeleportPlayer</code>, moves a player between dimensions, with cancellable enter/leave events and opt-in helpers (a portal-block base class and a chat-command builder).</li>
  <li><strong>Travel policy per dimension</strong> - pick the landing position (<code>SameCoordinates</code>, <code>DimensionSpawn</code>, or <code>LastVisited</code>) and optionally force a game mode.</li>
  <li><strong>Persistence</strong> - dimensions, generated chunks and per-player positions survive restarts. A dimension whose owning mod is removed is <strong>quarantined</strong>: its chunks are kept and transit is refused, never corrupted.</li>
  <li><strong>Zero Harmony patches</strong> - built entirely on the public <code>VintagestoryAPI</code>.</li>
</ul>
<h3>Quickstart</h3>
<p>Add Manifold as a dependency in your <code>modinfo.json</code>:</p>
<pre><code>"dependencies": { "manifold": "" }</code></pre>
<p>Then register a dimension from your mod system:</p>
<pre><code>var manifold = sapi.GetManifoldServer(this);
manifold.Registry
    .Define(new AssetLocation("mymod", "void"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .RegisterStatic();</code></pre>
<p>Full guide and API reference: <a href="https://pixnop.github.io/Manifold/">pixnop.github.io/Manifold</a></p>
<hr>
<h2>Demo</h2>
<p>A demo mod, <strong><a href="https://mods.vintagestory.at/manifoldsample">Manifold Sample</a></strong>, shows void, flat and streaming dimensions with the <code>/voiddim</code>, <code>/flatdim</code>, <code>/overworlddim</code> and <code>/streamdim</code> commands.</p>
<h2>Compatibility &amp; source</h2>
<ul>
  <li><strong>Vintage Story 1.21+</strong>, .NET 10, no Harmony required.</li>
  <li>Source: <a href="https://github.com/Pixnop/Manifold">github.com/Pixnop/Manifold</a></li>
  <li>Issues: <a href="https://github.com/Pixnop/Manifold/issues">issue tracker</a></li>
  <li><strong>MIT</strong> licensed.</li>
</ul>
```

---

# Page 2 - Manifold Sample (the demo, optional)

This is a working example, not a content mod. Publishing it is optional; it also ships
on the GitHub releases page.

| Field | Value |
|---|---|
| Name | `Manifold Sample` |
| URL Alias | `manifoldsample` |
| Side | `Universal` |
| Status | `Draft` first, then `Published` |
| Category | `Other` (it is a demo) |
| Tags | `Worldgen`, `teleport`, `Travel & Exploration` - NOT `Library` |

**Summary** (max 100 chars - 88):

```
Demo mod for the Manifold dimension API: void and flat dimensions with travel commands.
```

**Links**

| Field | Value |
|---|---|
| Homepage or Forum Post Url | `https://pixnop.github.io/Manifold/` |
| Source Code Url | `https://github.com/Pixnop/Manifold/tree/main/samples/ManifoldSample` |
| Issue tracker Url | `https://github.com/Pixnop/Manifold/issues` |

**Logo**: upload `manifoldsample-logo-480.png` (480x480) under Screenshots, then select it as the ModDB Logo.

**Release file**: `ManifoldSample-0.1.0.zip` from https://github.com/Pixnop/Manifold/releases, targeting VS 1.21.0.

### Description (HTML - paste in TinyMCE source view)

```html
<p><strong>Manifold Sample</strong> is a small demo mod that shows how to build custom dimensions with the <a href="https://mods.vintagestory.at/manifold">Manifold</a> library. It is a working example and smoke test - not a content mod.</p>
<p><strong>Requires <a href="https://mods.vintagestory.at/manifold">Manifold</a>.</strong></p>
<hr>
<h2>What it adds</h2>
<ul>
  <li>A <strong>void</strong> dimension (empty air) - <code>/voiddim</code>. Always lands you at a fixed spawn point.</li>
  <li>A <strong>flat</strong> dimension (granite floor) - <code>/flatdim</code>. Returns you to your last position there.</li>
  <li>A <strong>streaming</strong> dimension - <code>/streamdim</code>. Demonstrates opt-in streaming worldgen: chunks generate on demand as you walk, with no fixed region edge.</li>
  <li><code>/overworlddim</code> - travels back to the overworld, to your last position.</li>
  <li>A simple portal block built on Manifold's <code>PortalBlockBase</code>.</li>
</ul>
<h2>For developers</h2>
<p>Read the source as a copy-paste starting point for your own dimension mod:
<a href="https://github.com/Pixnop/Manifold/tree/main/samples/ManifoldSample">samples/ManifoldSample</a>.</p>
<h2>Compatibility</h2>
<p><strong>Vintage Story 1.21+</strong>. Universal. <strong>MIT</strong> licensed. Requires Manifold.</p>
```

---

## Releases (the "Add new Release" form)

For each mod, create a release: set the version, paste the changelog HTML (source view `<>`),
upload the matching zip, and select the supported game version (**1.21.0** and up).

### Manifold - version `0.1.0`

- **File**: `Manifold-0.1.0.zip` (from https://github.com/Pixnop/Manifold/releases)
- **For game version**: 1.21.0+

```html
<p>First public release.</p>
<ul>
  <li>Public API to declare <strong>custom dimensions</strong> (persistent or ephemeral; boot-time or runtime).</li>
  <li><strong>Active worldgen</strong> via <code>IWorldgenStrategy</code> with a configurable bounded region.</li>
  <li><strong>Player transit</strong> (<code>TeleportPlayer</code>) with cancellable enter/leave events; portal-block and chat-command helpers.</li>
  <li><strong>Travel policy</strong>: spawn behavior (SameCoordinates / DimensionSpawn / LastVisited) and an optional forced game mode.</li>
  <li><strong>Persistence</strong> of dimensions, generated chunks and per-player positions; quarantine for dimensions whose owning mod was removed.</li>
  <li>Built on the public API - zero Harmony patches.</li>
</ul>
<p>Built against the 1.21.0 API and validated in-game on 1.22.2 - works across 1.21 and 1.22.</p>
```

### Manifold Sample - version `0.1.0`

- **File**: `ManifoldSample-0.1.0.zip` (from https://github.com/Pixnop/Manifold/releases)
- **For game version**: 1.21.0+

```html
<p>First public release - a demo for the Manifold dimension API.</p>
<ul>
  <li>Void dimension (<code>/voiddim</code>) with a fixed spawn point.</li>
  <li>Flat dimension (<code>/flatdim</code>) with last-visited spawn.</li>
  <li><code>/overworlddim</code> to travel back to the overworld.</li>
  <li>A portal block built on <code>PortalBlockBase</code>.</li>
</ul>
<p>Requires Manifold. Built against 1.21.0, validated in-game on 1.22.2 (works on 1.21 and 1.22).</p>
```

## General steps

1. Create the mod, status **Draft**.
2. Upload the matching 480x480 logo under Screenshots; select it as the ModDB Logo.
3. Open the Text editor source view (`<>`), paste the HTML, save.
4. Upload the release zip as a version targeting VS 1.21.0.
5. Switch to **Published**.

> The two pages cross-link via alias URLs: Manifold's description links to
> `https://mods.vintagestory.at/manifoldsample`, and the Sample's links to
> `https://mods.vintagestory.at/manifold`. These resolve once each page is published with
> its alias - verify both links work after publishing.
> Optional slideshow screenshots: the flat dimension floor, the void dimension, a transit in action.
