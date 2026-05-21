# Getting Started

This guide shows how to add Manifold as a dependency, obtain the facade, register a dimension with a worldgen strategy, and expose a chat command to enter it. The complete example is a trimmed version of the bundled [ManifoldSample](https://github.com/Pixnop/Manifold/tree/main/samples/ManifoldSample).

## Prerequisites

- Vintage Story **1.21+** with `VintagestoryAPI.dll` available on the build path via `$VINTAGE_STORY`.
- Your mod targets **.NET 10** (`<TargetFramework>net10.0</TargetFramework>`).
- Manifold installed in the `Mods/` folder alongside your mod.

## 1. Declare the Dependency

In your mod's `modinfo.json`, add Manifold to the `dependencies` object. The empty string means "any version":

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

This ensures Vintage Story loads Manifold before your mod, and Manifold's `StartServerSide` has already run when yours executes.

To build against Manifold's API, add the [`Pixnop.Manifold`](https://www.nuget.org/packages/Pixnop.Manifold) NuGet package:

```sh
dotnet add package Pixnop.Manifold
```

The package contains only Manifold's API assembly (a compile-time reference); the running mod and its dependencies come from the Manifold mod installed in `Mods/`.

## 2. Set the Load Order

Manifold's `ModSystem` runs at order `0.05`. Return a value greater than that from `ExecuteOrder()` so Manifold is ready when your `StartServerSide` runs:

```csharp
public override double ExecuteOrder() => 0.5;
```

## 3. Get the Facade

Call the `GetManifoldServer(this)` extension method. Passing `this` (your `ModSystem`) tells Manifold to attribute any dimensions you register to your mod id - this is required for correct quarantine behavior if your mod is later uninstalled.

```csharp
public override void StartServerSide(ICoreServerAPI sapi)
{
    base.StartServerSide(sapi);

    var manifold = sapi.GetManifoldServer(this);
    if (!manifold.IsHealthy)
    {
        Mod.Logger.Warning("[MyMod] Manifold is unhealthy; dimension features disabled.");
        return;
    }
    // ...
}
```

`IsHealthy` is `true` when Manifold initialized successfully. Check it and bail out gracefully if it is `false`.

## 4. Register a Dimension

Use the fluent `Registry.Define(...)` builder to declare your dimension. You must supply a worldgen strategy and mark the lifetime (`Persistent` or `Ephemeral`) before calling `RegisterStatic()`.

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "void"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))
    .WithGenerationRadius(5)
    .RegisterStatic();
```

- `Persistent()` means the dimension survives server restarts and its chunks are saved.
- `WithGenerationRadius(5)` pre-generates an 11x11 chunk region (radius 5 in each direction) around the spawn point when the first player transits in.
- `WithFixedSpawn` sets `SpawnBehavior.DimensionSpawn` automatically and records the landing block position.

## 5. Add a Transit Command

`DimensionCommandBuilder` is a convenience helper that registers a VS chat command wired to `ITransitionService.TeleportPlayer`:

```csharp
new DimensionCommandBuilder()
    .Command("voiddim")
    .TargetDimension(new AssetLocation("mymod", "void"))
    .RequiresPrivilege("chat")
    .DescribedAs("Teleport to the void dimension.")
    .Register(sapi);
```

Players can now type `/voiddim` to transit.

## Complete Minimal Example

The following is a self-contained mod system that mirrors the ManifoldSample:

```csharp
using Manifold.Api.Helpers;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace MyMod;

public sealed class MyModSystem : ModSystem
{
    public override double ExecuteOrder() => 0.5;

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        base.StartServerSide(sapi);

        var manifold = sapi.GetManifoldServer(this);
        if (!manifold.IsHealthy)
        {
            Mod.Logger.Warning("[MyMod] Manifold unhealthy; dimension features disabled.");
            return;
        }

        // Register a void (all-air) dimension.
        manifold.Registry
            .Define(new AssetLocation("mymod", "void"))
            .Persistent()
            .WithWorldgen(new BasicVoidWorldgenStrategy())
            .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))
            .WithGenerationRadius(5)
            .RegisterStatic();

        // Register a teleport command.
        new DimensionCommandBuilder()
            .Command("voiddim")
            .TargetDimension(new AssetLocation("mymod", "void"))
            .RequiresPrivilege("chat")
            .DescribedAs("Teleport to the void dimension.")
            .Register(sapi);

        // Return to the overworld.
        new DimensionCommandBuilder()
            .Command("overworld")
            .TargetDimension(new AssetLocation("manifold", "overworld"))
            .RequiresPrivilege("chat")
            .WithSpawnBehavior(SpawnBehavior.LastVisited)
            .DescribedAs("Return to the overworld at your last position.")
            .Register(sapi);
    }
}
```

## Next Steps

- [Dimensions](dimensions.md) - understand dimension lifecycle and registry semantics.
- [Worldgen](worldgen.md) - implement a custom `IWorldgenStrategy`.
- [Transit & Travel Policy](transit-and-travel-policy.md) - fine-tune spawn behavior and build portal blocks.
