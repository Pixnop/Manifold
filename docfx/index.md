---
_layout: landing
title: Manifold
---
<div class="mf-hero mf-hero--dimension">
<div class="mf-hero__stage" id="mf-hero">
<img class="mf-hero__layer mf-hero__layer--bg" src="assets/site/hero-bg.png" width="1440" height="640" alt="" loading="eager" decoding="async">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-far.png">
<img class="mf-hero__layer mf-hero__layer--far" src="assets/site/hero-far.webp" width="1440" height="640" alt="" loading="eager" decoding="async">
</picture>
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-mid.png">
<img class="mf-hero__layer mf-hero__layer--mid" src="assets/site/hero-mid.webp" width="1440" height="640" alt="" loading="eager" decoding="async">
</picture>
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/hero-near.png">
<img class="mf-hero__layer mf-hero__layer--near" src="assets/site/hero-near.webp" width="1440" height="640" alt="" loading="eager" decoding="async">
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

Add Manifold to your `modinfo.json`, pull in the API package, and register a dimension.

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

<h2 class="mf-section-title mf-build-heading">What your mod gets</h2>
<p class="mf-section-sub">Nine pieces of a dimension API, each one a thing you would otherwise have to build yourself.</p>
<div class="mf-grid">
<a class="mf-card mf-card--island mf-tilt mf-floating mf-feature-card" href="articles/dimensions.md">
<img src="assets/moddb/feature-dimensions.png" width="220" height="160" alt="A dimension's registry entry, drawn as a floating chunk of world" loading="lazy">
<h3>Dimensions</h3>
<p>Declare a dimension statically at boot or dynamically at runtime, Persistent or Ephemeral, with a code-based registry and quarantine for uninstalled mods.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-1 mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/moddb/feature-worldgen.png">
<img src="assets/moddb/feature-worldgen.webp" width="220" height="160" alt="A dimension's terrain rising into place, chunk by chunk" loading="lazy">
</picture>
<h3>Active worldgen</h3>
<p>Manifold pre-generates a bounded region around the transit target before the player arrives, so nobody lands in a void at the world's negative edge.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-2 mf-feature-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/moddb/feature-transit.png">
<img src="assets/moddb/feature-transit.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="lazy">
</picture>
<h3>Transit and travel policy</h3>
<p>Teleport players, entities and single blocks between dimensions with cancellable events, plus per-dimension spawn behavior and an optional forced game mode.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-3 mf-feature-card" href="articles/dimensions.md">
<img src="assets/moddb/feature-safety.png" width="220" height="160" alt="A figure being evacuated from a dimension before it is removed" loading="lazy">
<h3>Safe teardown</h3>
<p>A dimension is never removed while a player is inside it. Occupants are evacuated first, and a player whose dimension is gone is rescued to the overworld on join.</p>
<div class="mf-console"><span class="mf-console__prompt">&gt;</span><code>/manifold purge mymod:void</code></div>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-pregeneration.png">
<img src="assets/site/highlight-pregeneration.webp" width="220" height="160" alt="A dimension's terrain generating with nobody there to see it" loading="lazy">
</picture>
<h3>Pregenerate on demand</h3>
<p><code>IManifoldServer.GenerateRegion</code> generates a dimension's spawn region with no player and no transit, for a boot-registered dimension or a test fixture.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-1 mf-feature-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-column-generated.png">
<img src="assets/site/highlight-column-generated.webp" width="220" height="160" alt="A marker settling onto a freshly generated chunk column" loading="lazy">
</picture>
<h3>Decorate as it generates</h3>
<p><code>IDimensionRegistry.ColumnGenerated</code> fires right after a brand-new column is generated, never for one only loaded from disk, so a strategy can drop a structure or loot without rewriting itself.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-2 mf-feature-card" href="articles/dimensions.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-client-metadata.png">
<img src="assets/site/highlight-client-metadata.webp" width="220" height="160" alt="A tagged packet travelling from a server island to a client island" loading="lazy">
</picture>
<h3>Client-side metadata</h3>
<p>Dimension metadata now replicates to client mirrors, and <code>IManifoldClient.LocalPlayerChangedDimension</code> fires when the local player transits.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-floating--delay-3 mf-feature-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-safe-landing.png">
<img src="assets/site/highlight-safe-landing.webp" width="220" height="160" alt="A figure landing on dry ground beside a pond, not in it" loading="lazy">
</picture>
<h3>Safe landing</h3>
<p>The default surface search requires two clear blocks above the landing spot and never drops a player into open water when dry ground is nearby.</p>
</a>
<a class="mf-card mf-card--island mf-tilt mf-floating mf-feature-card" href="articles/architecture.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/highlight-schema-saves.png">
<img src="assets/site/highlight-schema-saves.webp" width="220" height="160" alt="A save chest staying intact while its version tag glows" loading="lazy">
</picture>
<h3>Schema-versioned saves</h3>
<p>Every persisted blob carries an explicit schema version alongside it. A version this build does not recognize is refused and backed up, never misread.</p>
</a>
</div>

<img class="mf-divider" src="assets/site/divider.png" width="800" height="28" alt="" loading="lazy">

<h2 class="mf-section-title mf-build-heading">Mods built on Manifold</h2>
<p class="mf-section-sub">Consumer mods already shipping on Manifold's dimension API.</p>
<div class="mf-grid">
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/ppd">
<img src="https://moddbcdn.vintagestory.at/PageIcon_08d3978723446881640b30e4d2cb6569.png" width="44" height="44" alt="" loading="lazy">
<div><h3>Personal Pocket Dimension</h3><p>A private dimension of your own, reachable from anywhere.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/vsbackrooms">
<img src="https://moddbcdn.vintagestory.at/PageIcon_e1e18780f2ae218ab2b48522e7c08be9.png" width="44" height="44" alt="" loading="lazy">
<div><h3>VS Backrooms</h3><p>An endless, unsettling liminal dimension to get lost in.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/show/mod/56037">
<img src="https://moddbcdn.vintagestory.at/11111_6c0404dad260424dbe3bd60a0c8a7367.jpg" width="44" height="44" alt="" loading="lazy">
<div><h3>Spaturno and Trini's Secure Shelter</h3><p>A safehouse dimension, sealed off from the world outside.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/show/mod/35893">
<img src="https://moddbcdn.vintagestory.at/sixth-history-logo_36e5b4ecbeabcdbd7f70526bd5c0e170.png" width="44" height="44" alt="" loading="lazy">
<div><h3>Esoterica</h3><p>Occult dimensions and rituals built on Manifold's transit API.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/chart">
<img src="https://moddbcdn.vintagestory.at/chart-logo-480_aa3568eef569b63088ec5d5b7784bea0.png" width="44" height="44" alt="" loading="lazy">
<div><h3>Chart</h3><p>A companion mod, its name the other kind of atlas Manifold is named after.</p></div>
</a>
<a class="mf-card mf-tilt mf-mod-card" href="https://mods.vintagestory.at/manifoldsample">
<img src="https://moddbcdn.vintagestory.at/manifoldsample-logo-_3d1415ffa12c25e9ccbdf51f26681eda.png" width="44" height="44" alt="" loading="lazy">
<div><h3>Manifold Sample</h3><p>The working demo mod this documentation's own examples are trimmed from.</p></div>
</a>
</div>

<img class="mf-divider" src="assets/site/divider.png" width="800" height="28" alt="" loading="lazy">

<h2 class="mf-section-title mf-build-heading">Browse the docs</h2>
<p class="mf-section-sub">Every article, with the API reference alongside.</p>
<div class="mf-grid">
<a class="mf-card mf-tilt mf-doc-card" href="articles/getting-started.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-getting-started.png">
<img src="assets/site/header-getting-started.webp" width="220" height="160" alt="A portal gate assembling itself stone by stone" loading="lazy">
</picture>
<h3>Getting Started</h3>
<p>Dependency wiring, load order, and a minimal consumer mod.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/dimensions.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-dimensions.png">
<img src="assets/site/header-dimensions.webp" width="220" height="160" alt="Two settled islands and a third, ephemeral one fading in and out" loading="lazy">
</picture>
<h3>Dimensions</h3>
<p>Lifecycle, registry, codes, quarantine.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/worldgen.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-worldgen.png">
<img src="assets/site/header-worldgen.webp" width="220" height="160" alt="A chunk of terrain building itself, block by block" loading="lazy">
</picture>
<h3>Worldgen</h3>
<p><code>IWorldgenStrategy</code> and the active generation model.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/transit-and-travel-policy.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-transit-and-travel-policy.png">
<img src="assets/site/header-transit-and-travel-policy.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="lazy">
</picture>
<h3>Transit &amp; Travel Policy</h3>
<p>Teleport API, spawn behaviors, helpers.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="articles/architecture.md">
<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="assets/site/header-architecture.png">
<img src="assets/site/header-architecture.webp" width="220" height="160" alt="Three islands linked by transit arcs" loading="lazy">
</picture>
<h3>Architecture</h3>
<p>Internal services, the sided split, the zero-Harmony note.</p>
</a>
<a class="mf-card mf-tilt mf-doc-card" href="api/Manifold.Api.yml">
<h3>API Reference</h3>
<p>Auto-generated from the XML documentation on every public member.</p>
</a>
</div>

<div class="mf-why">
<p>A manifold, in mathematics, is a space stitched together from local, ordinary-looking pieces, described by an atlas of charts. In an electrical or plumbing system, a manifold is the part that routes one flow out to several outlets. Both readings fit: each dimension is a self-contained piece charted into the same registry, and Manifold itself is the fitting that routes one API call out to whichever dimension it names.</p>
</div>

## Compatibility

- Vintage Story **1.22.x** (the integration suite runs on 1.22.7)
- **.NET 10**
- No Harmony reference; protobuf is provided by the game itself

## License

[MIT](https://github.com/Pixnop/Manifold/blob/main/LICENSE)
