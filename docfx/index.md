---
_layout: landing
title: Manifold
---
<div class="mf-hero mf-hero--dimension">
<div class="mf-hero__stage" id="mf-hero">
<img class="mf-hero__layer mf-hero__layer--bg mf-hero__layer--bg-dark" src="assets/site/hero-bg.webp" width="1440" height="640" alt="" loading="eager" decoding="async">
<img class="mf-hero__layer mf-hero__layer--bg mf-hero__layer--bg-light" src="assets/site/hero-bg-light.webp" width="1440" height="640" alt="" loading="eager" decoding="async">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-far.png">
<img class="mf-hero__layer mf-hero__layer--far" src="assets/site/hero-far.webp" srcset="assets/site/hero-far-720.webp 1440w, assets/site/hero-far.webp 2880w" sizes="(max-width: 768px) 100vw, 1440px" width="1440" height="640" alt="" loading="eager" decoding="async">
</picture>
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-mid.png">
<img class="mf-hero__layer mf-hero__layer--mid" src="assets/site/hero-mid.webp" srcset="assets/site/hero-mid-720.webp 1440w, assets/site/hero-mid.webp 2880w" sizes="(max-width: 768px) 100vw, 1440px" width="1440" height="640" alt="" loading="eager" decoding="async">
</picture>
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-near.png">
<img class="mf-hero__layer mf-hero__layer--near" src="assets/site/hero-near.webp" srcset="assets/site/hero-near-720.webp 1440w, assets/site/hero-near.webp 2880w" sizes="(max-width: 768px) 100vw, 1440px" width="1440" height="640" alt="" loading="eager" decoding="async">
</picture>
<canvas class="mf-hero__sparks" id="mf-hero-sparks" width="1440" height="640" aria-hidden="true"></canvas>
<a class="mf-hero__portal-link" href="articles/getting-started.md" aria-label="Enter Manifold: read the Getting Started guide">
<span class="mf-portal-glow mf-hero__portal-glow" aria-hidden="true"></span>
</a>
</div>
<h1 class="mf-hero__title">Manifold</h1>
<p class="mf-hero__tagline">A Vintage Story 1.22 library mod for declaring and managing custom dimensions: worldgen, transit, travel policy, no Harmony required.</p>
<div class="mf-hero__ctas">
<a class="mf-btn mf-btn--primary" href="articles/getting-started.md">Get started</a>
<a class="mf-btn mf-btn--ghost" href="api/Manifold.Api.yml">API reference</a>
<a class="mf-btn mf-btn--ghost" href="https://mods.vintagestory.at/manifold">Mod DB</a>
<a class="mf-btn mf-btn--ghost" href="https://github.com/Pixnop/Manifold">GitHub</a>
<a class="mf-btn mf-btn--ghost" href="https://www.nuget.org/packages/Pixnop.Manifold">NuGet</a>
</div>
</div>

<h2 class="mf-section-title mf-build-heading no-anchor">Three steps in</h2>
<p class="mf-section-sub">Add Manifold to your <code>modinfo.json</code>, pull in the API package, and register a dimension.</p>
<div class="mf-install">

```json
{
  "modid": "mymod",
  "dependencies": {
    "game": "1.22.0",
    "manifold": ""
  }
}
```

```sh
dotnet add package Pixnop.Manifold
```

```csharp
var manifold = sapi.GetManifoldServer(this);

manifold.Registry
    .Define(new AssetLocation("mymod", "void"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))
    .WithGenerationRadius(5)
    .RegisterStatic();
```

The full walkthrough, load order and all, is in [Getting Started](articles/getting-started.md).

</div>

<div class="mf-drift-zone" aria-hidden="true">
<img class="mf-drift" src="assets/site/drift-1.png" width="140" height="96" style="left: 4%; top: -3rem;" data-drift-speed="0.1" alt="" loading="lazy">
</div>

<h2 class="mf-section-title mf-build-heading no-anchor">What your mod gets</h2>
<p class="mf-section-sub">A dimension API assembled from pieces you would otherwise build yourself, one at a time.</p>
<div class="mf-grid">
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/dimensions.md">
<img src="assets/moddb/feature-dimensions.png" width="220" height="160" alt="A dimension's registry entry, drawn as a floating chunk of world" loading="lazy">
<h3 class="no-anchor">Dimensions</h3>
<p>Declare a dimension statically at boot or dynamically at runtime, Persistent or Ephemeral, with a code-based registry and quarantine for uninstalled mods.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/moddb/feature-worldgen.png">
<img src="assets/moddb/feature-worldgen.webp" width="220" height="160" alt="A dimension's terrain rising into place, chunk by chunk" loading="lazy">
</picture>
<h3 class="no-anchor">Active worldgen</h3>
<p>Manifold pre-generates a bounded region around the transit target before the player arrives, so they land on terrain, not in an ungenerated void (the world's negative corner excepted).</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/moddb/feature-transit.png">
<img src="assets/moddb/feature-transit.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="lazy">
</picture>
<h3 class="no-anchor">Transit and travel policy</h3>
<p>Teleport players, entities and single blocks between dimensions with cancellable events, plus per-dimension spawn behavior and an optional forced game mode.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/dimensions.md">
<img src="assets/moddb/feature-safety.png" width="220" height="160" alt="A figure being evacuated from a dimension before it is removed" loading="lazy">
<h3 class="no-anchor">Safe teardown</h3>
<p>A dimension is never removed while a player is inside it. Occupants are evacuated first, and a player whose dimension is gone is rescued to the overworld on join.</p>
<div class="mf-console"><span class="mf-console__prompt">&gt;</span><code>/manifold purge mymod:void</code></div>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-pregeneration.png">
<img src="assets/site/highlight-pregeneration.webp" width="220" height="160" alt="A dimension's terrain generating with nobody there to see it" loading="lazy">
</picture>
<h3 class="no-anchor">Pregenerate on demand</h3>
<p><code>IManifoldServer.GenerateRegion</code> generates a dimension's spawn region with no player and no transit, for a boot-registered dimension or a test fixture.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-column-generated.png">
<img src="assets/site/highlight-column-generated.webp" width="220" height="160" alt="A marker settling onto a freshly generated chunk column" loading="lazy">
</picture>
<h3 class="no-anchor">Decorate as it generates</h3>
<p><code>IDimensionRegistry.ColumnGenerated</code> fires right after a brand-new column is generated, never for one only loaded from disk, so a strategy can drop a structure or loot without rewriting itself.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/dimensions.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-client-metadata.png">
<img src="assets/site/highlight-client-metadata.webp" width="220" height="160" alt="A tagged packet travelling from a server island to a client island" loading="lazy">
</picture>
<h3 class="no-anchor">Client-side metadata</h3>
<p>Dimension metadata now replicates to client mirrors, and <code>IManifoldClient.LocalPlayerChangedDimension</code> fires when the local player transits.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-safe-landing.png">
<img src="assets/site/highlight-safe-landing.webp" width="220" height="160" alt="A figure landing on dry ground beside a pond, not in it" loading="lazy">
</picture>
<h3 class="no-anchor">Safe landing</h3>
<p>The default surface search requires two clear blocks above the landing spot and only settles on water when that column has no dry ground above it.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-feature-card" href="articles/architecture.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-schema-saves.png">
<img src="assets/site/highlight-schema-saves.webp" width="220" height="160" alt="A save chest staying intact while its version tag glows" loading="lazy">
</picture>
<h3 class="no-anchor">Schema-versioned saves</h3>
<p>Every persisted blob carries an explicit schema version alongside it. A version this build does not recognize is refused and backed up, never misread.</p>
</a>
</div>

<div class="mf-also">
<div class="mf-card"><strong>Streaming worldgen</strong><p><code>.Streaming(loadRadius)</code> generates chunks on demand as players move, with no invisible walls at the edge.</p></div>
<div class="mf-card"><strong>Per-dimension inventory</strong><p><code>WithSeparateInventory</code> swaps the hotbar, backpack or character slots per dimension, with no item loss.</p></div>
<div class="mf-card"><strong>Dark dimensions</strong><p><code>WithDarkSky(ceilingY)</code> seals a dimension under an opaque ceiling, lit only by block light.</p></div>
<div class="mf-card"><strong>Relight on demand</strong><p><code>RelightRegion</code> and <code>/manifold relight</code> recalculate light after a mod places blocks in a custom dimension.</p></div>
<div class="mf-card"><strong>Savegame persistence</strong><p>The manifest, generated-column set and per-player last-visited positions all survive a restart.</p></div>
<div class="mf-card"><strong>Opt-in helpers</strong><p><code>PortalBlockBase</code> and <code>DimensionCommandBuilder</code> cut the boilerplate for a portal block or a transit command.</p></div>
</div>

<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/divider.png">
<img class="mf-divider" src="assets/site/divider.webp" width="800" height="28" alt="" loading="lazy">
</picture>

<div class="mf-drift-zone" aria-hidden="true">
<img class="mf-drift" src="assets/site/drift-2.png" width="130" height="90" style="right: 6%; top: -2.5rem;" data-drift-speed="-0.07" alt="" loading="lazy">
</div>

<h2 class="mf-section-title mf-build-heading no-anchor">Mods built on Manifold</h2>
<p class="mf-section-sub">Consumer mods already shipping on Manifold's dimension API.</p>
<div class="mf-grid">
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/ppd">
<img src="https://moddbcdn.vintagestory.at/PageIcon_08d3978723446881640b30e4d2cb6569.png" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">Personal Pocket Dimension</h3><p>A private dimension of your own, reachable from anywhere.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/vsbackrooms">
<img src="https://moddbcdn.vintagestory.at/PageIcon_e1e18780f2ae218ab2b48522e7c08be9.png" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">VS Backrooms</h3><p>An endless, unsettling liminal dimension to get lost in.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/show/mod/56037">
<img src="https://moddbcdn.vintagestory.at/11111_6c0404dad260424dbe3bd60a0c8a7367.jpg" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">Spaturno and Trini's Secure Shelter</h3><p>A safehouse dimension, sealed off from the world outside.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/show/mod/35893">
<img src="https://moddbcdn.vintagestory.at/sixth-history-logo_36e5b4ecbeabcdbd7f70526bd5c0e170.png" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">Esoterica</h3><p>Occult dimensions and rituals built on Manifold's transit API.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/chart">
<img src="https://moddbcdn.vintagestory.at/chart-logo-480_aa3568eef569b63088ec5d5b7784bea0.png" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">Chart</h3><p>A dimension-aware world map: per-dimension tiles, hot-swapped on transit.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/manifoldsample">
<img src="https://moddbcdn.vintagestory.at/manifoldsample-logo-_3d1415ffa12c25e9ccbdf51f26681eda.png" width="44" height="44" alt="" loading="lazy">
<div><h3 class="no-anchor">Manifold Sample</h3><p>The working demo mod this documentation's own examples are trimmed from.</p></div>
</a>
</div>

<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/divider.png">
<img class="mf-divider" src="assets/site/divider.webp" width="800" height="28" alt="" loading="lazy">
</picture>

<div class="mf-drift-zone" aria-hidden="true">
<img class="mf-drift" src="assets/site/drift-3.png" width="120" height="84" style="left: 46%; top: -2rem;" data-drift-speed="0.09" alt="" loading="lazy">
</div>

<h2 class="mf-section-title mf-build-heading no-anchor">Browse the docs</h2>
<p class="mf-section-sub">Every article, with the API reference alongside.</p>
<div class="mf-grid">
<a class="mf-card mf-tilt mf-doc-card" href="articles/getting-started.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-getting-started.png">
<img src="assets/site/header-getting-started.webp" width="220" height="160" alt="A portal gate assembling itself stone by stone" loading="lazy">
</picture>
<h3 class="no-anchor">Getting Started</h3>
<p>Dependency wiring, load order, and a minimal consumer mod.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/dimensions.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-dimensions.png">
<img src="assets/site/header-dimensions.webp" width="220" height="160" alt="Two settled islands and a third, ephemeral one fading in and out" loading="lazy">
</picture>
<h3 class="no-anchor">Dimensions</h3>
<p>Lifecycle, registry, codes, quarantine.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-worldgen.png">
<img src="assets/site/header-worldgen.webp" width="220" height="160" alt="A chunk of terrain building itself, block by block" loading="lazy">
</picture>
<h3 class="no-anchor">Worldgen</h3>
<p><code>IWorldgenStrategy</code> and the active generation model.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-transit-and-travel-policy.png">
<img src="assets/site/header-transit-and-travel-policy.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="lazy">
</picture>
<h3 class="no-anchor">Transit &amp; Travel Policy</h3>
<p>Teleport API, spawn behaviors, helpers.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/architecture.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-architecture.png">
<img src="assets/site/header-architecture.webp" width="220" height="160" alt="Three islands linked by transit arcs" loading="lazy">
</picture>
<h3 class="no-anchor">Architecture</h3>
<p>Internal services, the sided split, the zero-Harmony note.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="api/Manifold.Api.yml">
<img src="assets/site/api-reference.png" width="220" height="160" alt="A book resting open on a lectern, on its own small island" loading="lazy">
<h3 class="no-anchor">API Reference</h3>
<p>Auto-generated from the XML documentation on every public member.</p>
</a>
</div>

<div class="mf-why">
<p>A manifold, in mathematics, is a space stitched together from local, ordinary-looking pieces, described by an atlas of charts. In an electrical or plumbing system, a manifold is the part that routes one flow out to several outlets. Both readings fit: each dimension is a self-contained piece charted into the same registry, and Manifold itself is the fitting that routes one API call out to whichever dimension it names.</p>
</div>

<div class="mf-close">
<div class="mf-close__col">
<h3 class="no-anchor">Compatibility</h3>
<ul>
<li>Vintage Story <strong>1.22.x</strong> (the integration suite runs on 1.22.7)</li>
<li><strong>.NET 10</strong></li>
<li>No Harmony reference; protobuf is provided by the game itself</li>
</ul>
</div>
<div class="mf-close__col">
<h3 class="no-anchor">License</h3>
<p><a href="https://github.com/Pixnop/Manifold/blob/main/LICENSE">MIT</a></p>
</div>
</div>
