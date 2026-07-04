<p align="center">
  <img src="manifold.svg" width="180" alt="Manifold logo" />
</p>

# Manifold

**A Vintage Story 1.21+ library mod for declaring and managing custom dimensions.**

[![Mod DB](https://img.shields.io/badge/Mod_DB-Manifold-1E9FE3)](https://mods.vintagestory.at/manifold)
[![NuGet](https://img.shields.io/nuget/vpre/Pixnop.Manifold?label=nuget)](https://www.nuget.org/packages/Pixnop.Manifold)
[![Build](https://img.shields.io/github/actions/workflow/status/Pixnop/Manifold/ci.yml?branch=main&label=build)](https://github.com/Pixnop/Manifold/actions)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Manifold&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Pixnop_Manifold)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Manifold&metric=coverage)](https://sonarcloud.io/summary/new_code?id=Pixnop_Manifold)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

---

## Download

- **Players:** [Manifold on the Mod DB](https://mods.vintagestory.at/manifold) - install this (the library other mods depend on). Optional demo: [Manifold Sample](https://mods.vintagestory.at/manifoldsample).
- **Mod developers:** reference the API at compile time from NuGet - `dotnet add package Pixnop.Manifold`. Your mod still declares `manifold` as a runtime dependency in `modinfo.json`.

### Companion mods

- **[Chart](https://mods.vintagestory.at/chart)** (0.1.0, alpha) - dimension-aware world map. Per-dimension tile cache, vanilla-style rendering pipeline (palette + hillshade + blur), hot-swap on transit. Client-side only. Requires Manifold 0.3.1+. Source under [`companions/Chart/`](companions/Chart/).

---

## Features

- **Custom dimensions** - declare persistent or ephemeral dimensions from any mod; boot-time (`RegisterStatic`) or runtime (`Create`).
- **Active worldgen** - two modes, both configurable per dimension. **Bounded** (default): Manifold pre-generates a fixed chunk region around the transit target before the player arrives, radius set via `WithGenerationRadius`. **Streaming** (opt-in): call `.Streaming(loadRadius)` and Manifold generates chunks on demand as players move, with no invisible walls at a region edge. The streaming radius is extended to the server view distance so generated terrain always reaches as far as the player can see. The relight band height is set per dimension via `WithRelightHeight` (default 20).
- **Player transit** - `ITransitionService.TeleportPlayer` moves a player between any two dimensions with a single call.
- **Entity transit** - `ITransitionService.TeleportEntity` moves non-player entities (dropped items, mobs) between dimensions; the destination region is generated on demand before the entity is re-homed.
- **Block transit** (0.4.0) - `ITransitionService.TeleportBlock(source, targetDim, targetLocal)` moves a single block plus its `BlockEntity` state (inventory, attributes, BE-behaviors) between dimensions. Completes the Player / Entity / Block triplet; the destination region is generated on demand and the BE state is round-tripped through `ToTreeAttributes` / `FromTreeAttributes`.
- **Transit events** (0.4.0) - `PlayerEntering` (pre-generation, cancellable), `PlayerArriving` (post-generation, pre-teleport, cancellable), `PlayerLeft` / `PlayerEntered` (post-teleport), and `EntityChangedDimension` (post `TeleportEntity` for non-player entities).
- **Travel policy per dimension** - spawn behavior (`SameCoordinates` / `DimensionSpawn` / `LastVisited`), optional forced game mode, all configured through a fluent builder.
- **Per-dimension inventory** (opt-in) - `WithSeparateInventory(ManifoldInventory.Hotbar | Backpack | Character)` gives a dimension its own player inventory for the chosen categories. Entering swaps to the dimension's set (empty on the first visit), leaving restores the previous one. Stored in player moddata so it survives logout and restarts, with no item loss.
- **Per-dimension metadata** (0.4.0) - `.WithMetadata(key, value)` attaches typed registration-time hints to a dimension; consumers read them via `IDimension.Metadata` or the typed `GetMetadata<T>(key, defaultValue)` extension. Supports primitives, `string`, `enum`, `byte[]`, and `null`.
- **Per-dimension streaming budget** (0.4.0) - `.WithStreamingBudget(maxColumnsPerTick)` (range 1..64) caps how many columns the streaming driver may ensure for that dimension per tick. Budgets are independent so a busy dim cannot starve a quiet one.
- **Persistence** - dimension manifest, generated-column set, and per-player last-visited positions survive server restarts. Dimensions from uninstalled mods are quarantined (chunks kept, transit refused).
- **Client mirror** - the dimension list is replicated to connected clients via `IManifoldClient`.
- **Zero Harmony patches** - built entirely on the public `VintagestoryAPI`. 0Harmony and protobuf are provided by the game and not patched.
- **Opt-in helpers** - `PortalBlockBase`, `DimensionCommandBuilder`, `BasicVoidWorldgenStrategy` to get started with minimal boilerplate.

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

# Run the integration scenarios (boots a real headless VS server per test class;
# see tests/Manifold.Scenarios/README.md)
dotnet test tests/Manifold.Scenarios

# Build the documentation site (requires docfx installed globally)
docfx docfx/docfx.json
```

---

## License

[MIT](LICENSE)
