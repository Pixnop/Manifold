<p align="center">
  <img src="manifold.svg" width="180" alt="Manifold logo" />
</p>

# Manifold

**A Vintage Story 1.21+ library mod for declaring and managing custom dimensions.**

[![Mod DB](https://img.shields.io/badge/Mod_DB-Manifold-1E9FE3)](https://mods.vintagestory.at/manifold)
[![Build](https://img.shields.io/github/actions/workflow/status/Pixnop/Manifold/ci.yml?branch=main&label=build)](https://github.com/Pixnop/Manifold/actions)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Manifold&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Pixnop_Manifold)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Manifold&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Pixnop_Manifold)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

---

## Download

- **[Manifold on the Mod DB](https://mods.vintagestory.at/manifold)** - install this (the library other mods depend on).
- **[Manifold Sample](https://mods.vintagestory.at/manifoldsample)** - optional demo mod (void + flat dimensions, travel commands).

---

## Features

- **Custom dimensions** - declare persistent or ephemeral dimensions from any mod; boot-time (`RegisterStatic`) or runtime (`Create`).
- **Active worldgen** - two modes, both configurable per dimension. **Bounded** (default): Manifold pre-generates a fixed chunk region around the transit target before the player arrives, radius set via `WithGenerationRadius`. **Streaming** (opt-in): call `.Streaming(loadRadius)` and Manifold generates chunks on demand as players move, keeping a window of `loadRadius` chunks generated around each player - no invisible walls at a region edge.
- **Player transit** - `ITransitionService.TeleportPlayer` moves a player between any two dimensions with a single call.
- **Travel policy per dimension** - spawn behavior (`SameCoordinates` / `DimensionSpawn` / `LastVisited`), optional forced game mode, all configured through a fluent builder.
- **Persistence** - dimension manifest, generated-column set, and per-player last-visited positions survive server restarts. Dimensions from uninstalled mods are quarantined (chunks kept, transit refused).
- **Client mirror** - the dimension list is replicated to connected clients via `IManifoldClient`.
- **Zero Harmony patches** - built entirely on the public `VintagestoryAPI`. 0Harmony and protobuf are provided by the game and not patched.
- **Opt-in helpers** - `PortalBlockBase`, `DimensionCommandBuilder`, `BasicVoidWorldgenStrategy` to get started with minimal boilerplate.

> **Streaming note:** Opt-in streaming worldgen is now available via `.Streaming(loadRadius)` and will ship in the next release. Two refinements are still planned: configurable relight height (currently fixed at Y0-20, so content above Y20 is under-lit until the engine relights naturally) and tying the load radius to the client render distance.

---

## Quickstart

### 1. Declare the dependency in `modinfo.json`

```json
{
  "modid": "mymod",
  "name": "My Mod",
  "version": "1.0.0",
  "dependencies": {
    "game": "1.21.0",
    "manifold": ""
  }
}
```

### 2. Register a dimension and a transit command

```csharp
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

public sealed class MyModSystem : ModSystem
{
    public override double ExecuteOrder() => 0.5; // run after Manifold (0.05)

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        base.StartServerSide(sapi);

        var manifold = sapi.GetManifoldServer(this); // owner-scoped facade
        if (!manifold.IsHealthy) return;

        manifold.Registry
            .Define(new AssetLocation("mymod", "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))
            .WithGenerationRadius(5)
            .RegisterStatic();

        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation("mymod", "void"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the void dimension.")
            .Register(sapi);
    }
}
```

---

## Compatibility

| Requirement | Version |
|-------------|---------|
| Vintage Story | 1.21+ |
| .NET | 10 |
| Harmony | Not required (0Harmony provided by the game) |
| protobuf-net | Not required (bundled with the game) |

---

## Documentation

- Full documentation site: **https://pixnop.github.io/Manifold/**
- Conceptual articles: [`docfx/articles/`](docfx/articles/)
  - [Getting Started](docfx/articles/getting-started.md)
  - [Dimensions](docfx/articles/dimensions.md)
  - [Worldgen](docfx/articles/worldgen.md)
  - [Transit & Travel Policy](docfx/articles/transit-and-travel-policy.md)
  - [Architecture](docfx/articles/architecture.md)

---

## Building from Source

Requires a local Vintage Story install with the `VINTAGE_STORY` environment variable pointing to the game directory (the folder containing `VintagestoryAPI.dll`).

```sh
# Build all projects
dotnet build -c Release

# Run pure (non-VS-runtime) unit tests
dotnet test tests/Manifold.Pure.Tests

# Run with code coverage
dotnet test tests/Manifold.Pure.Tests --collect:"XPlat Code Coverage"

# Build the documentation site (requires docfx installed globally)
docfx docfx/docfx.json
```

---

## License

[MIT](LICENSE)
