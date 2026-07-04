# Worldgen

Manifold uses an **active, bounded-region generation model**. Rather than waiting for the chunk streaming engine to request columns on demand, Manifold pre-generates a square region of chunks around the transit target before the player arrives. This guarantees the player always lands on solid (or at least well-defined) terrain.

## IWorldgenStrategy

Every dimension must have exactly one `IWorldgenStrategy` attached via `IDimensionBuilder.WithWorldgen`. The interface has two methods:

```csharp
public interface IWorldgenStrategy
{
    // Called once per dimension before the first column is generated.
    // Resolve block ids here - do NOT resolve them in GenerateColumn (performance).
    void OnInitialize(IWorldgenInitContext ctx);

    // Fill one chunk column with blocks.
    // Use ctx.BlockAccessor.SetBlock with dimension-encoded positions.
    void GenerateColumn(IWorldgenChunkContext ctx);
}
```

### OnInitialize

Called once, on the first transit into the dimension (lazy initialization). Use `IWorldgenInitContext` to resolve block ids from the `IBlockAccessor` or `IWorldAccessor`. Store them in fields for later use in `GenerateColumn`.

```csharp
public sealed class MyFloorStrategy : IWorldgenStrategy
{
    private int _stoneBlockId;

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        _stoneBlockId = ctx.BlockAccessor.GetBlock(new AssetLocation("game", "rock-granite")).Id;
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        // Fill y=0..63 with stone in this chunk column.
        for (int x = 0; x < 32; x++)
        for (int z = 0; z < 32; z++)
        for (int y = 0; y < 64; y++)
        {
            int wx = ctx.ChunkX * 32 + x;
            int wz = ctx.ChunkZ * 32 + z;
            var pos = new BlockPos(wx, y, wz, ctx.DimensionId);
            ctx.BlockAccessor.SetBlock(_stoneBlockId, pos);
        }
    }
}
```

### GenerateColumn

Called once per chunk column within the generation radius. The `IWorldgenChunkContext` provides:

| Property | Description |
|----------|-------------|
| `DimensionId` | Engine dimension id - use as the 4th argument to `new BlockPos(x, y, z, DimensionId)`. |
| `ChunkX` / `ChunkZ` | Chunk-grid coordinates. Multiply by 32 to get the world-space origin of the column. |
| `BlockAccessor` | Write blocks with `SetBlock(blockId, pos)`. Positions **must** be dimension-encoded. |
| `Rng` | `LCGRandom` seeded deterministically per column - use for reproducible procedural generation. |

> **Important:** Always dimension-encode positions. A `BlockPos` without `DimensionId` defaults to dimension 0 (the overworld) and will silently write to the wrong world.

## Active Bounded-Region Generation

When a player transits into a dimension for the first time (or after a server restart for a Persistent dimension), Manifold:

1. Computes the target chunk column from the transit destination position.
2. Iterates all columns within `generationRadius` chunks in X and Z.
3. For each column: calls `CreateChunkColumnForDimension`, then `strategy.GenerateColumn`, then relights the column and sends it to the client.
4. Completes the player teleport once generation finishes.

This happens **synchronously on the main thread** before the player arrives - so the player never sees an ungenerated void.

> **Generation is transit-driven - registration alone creates no terrain.** `RegisterStatic()` and
> `Create()` only record the dimension; `WithFixedSpawn` and `WithGenerationRadius` describe what to
> generate once a transit happens, not when. Until the first `TeleportPlayer`/`TeleportEntity`/
> `TeleportBlock` targets the dimension (or a `.Streaming(...)` driver picks it up), every position
> reads as air. If your mod needs content to exist before the first arrival - a spawn platform, a
> prebuilt hub - trigger the generation yourself right after registration; a no-op `TeleportBlock`
> from a guaranteed-air source into the dimension is the current supported way (see issue #69 for
> the planned first-class API).

## WithGenerationRadius

The radius (in chunks) is configured on the builder:

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "dungeon"))
    .Persistent()
    .WithWorldgen(new DungeonWorldgenStrategy())
    .WithGenerationRadius(3)   // 7x7 columns (3 in each direction + center)
    .RegisterStatic();
```

- Default is **2** (5x5 columns).
- Range: **0** (center column only) to **16** (33x33 columns).
- Larger radii cost more time per transit. For most dimensions, 2-5 is sufficient.

Already-generated columns are tracked in the savegame and are not re-generated on subsequent server starts.

## BasicVoidWorldgenStrategy

Manifold ships a built-in no-op strategy for air-filled dimensions:

```csharp
.WithWorldgen(new BasicVoidWorldgenStrategy())
```

`OnInitialize` and `GenerateColumn` are both empty. The allocated chunks contain only air blocks, which is the VS default for a freshly allocated column.

## Streaming Worldgen

Streaming is an opt-in alternative to bounded generation. Instead of pre-generating a fixed region and stopping, Manifold continuously generates chunks on demand as players move, keeping a window of `loadRadius` chunks generated around each player.

### Opting in

Call `.Streaming(loadRadius)` on the builder instead of (or in addition to) a large `WithGenerationRadius`:

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "openworld"))
    .Persistent()
    .WithWorldgen(new MyOpenWorldStrategy())
    .WithGenerationRadius(2)   // landing pad: 5x5 columns generated synchronously on first transit
    .Streaming(8)              // streaming window: 8 chunks around each player
    .RegisterStatic();
```

`loadRadius` must be in the range **1..32**.

### How transit works in a streaming dimension

When a player enters a streaming dimension:

1. The synchronous landing pad is generated first (`WithGenerationRadius` region, default 2). The player is teleported only after this completes, so they never spawn in void.
2. The streaming driver then fills the surrounding window (`loadRadius`) over subsequent server ticks, spread across a per-tick budget so the server does not hitch.
3. As the player moves, newly entered chunks are queued and generated the same way.
4. Distant chunks that fall outside the window are handled by the engine's normal chunk unloading. Blocks placed in those chunks survive unload and reload correctly.

### Bounded vs streaming comparison

| | Bounded (default) | Streaming (opt-in) |
|---|---|---|
| Builder method | `WithGenerationRadius(r)` | `.Streaming(loadRadius)` |
| When chunks are generated | Synchronously at transit | On demand as players move |
| Region size | Fixed `(2r+1) x (2r+1)` columns | Follows each player continuously |
| Walking past the edge | Ungenerated (void) | No edge - new chunks stream in |
| Best for | Dungeons, arenas, lobby spaces | Open-world or exploration dimensions |

The two modes coexist. A dimension can use only bounded generation, only streaming, or both (bounded for the initial landing pad, streaming for ongoing movement).

### Relight height

The relight pass covers Y0 up to the dimension's relight height (default 20), set with
`WithRelightHeight(maxY)`. Content built above that height is under-lit until the engine performs
its own relight pass naturally, so set a taller band for dimensions with tall terrain or structures.
A tighter band is cheaper to relight, which matters most for streaming (relit per tick).

**Symptom checklist:** if light inside your dimension looks wrong (stale block light, dark
structures, light only fixing itself when you break and re-place a light source), the first thing
to check is whether your content sits above the relight band. A hut at Y 65 with the default band
of 20 is entirely outside the relit volume.

**Day/night inside custom dimensions:** once a dimension is correctly relit, sky-exposed blocks
carry full sunlight values, and the engine currently renders them at full brightness regardless
of the time of day - custom dimensions do not follow the overworld's day/night cycle visually.
There is no public hook to attenuate this per dimension today. If your dimension previously
looked dark and you were compensating for it, expect it to be properly lit from Manifold 0.4.2.
Per-dimension time of day (which would give real nights) is tracked in
[issue #55](https://github.com/Pixnop/Manifold/issues/55).

### Relighting at runtime (`RelightRegion`)

Worldgen relight only covers what the strategy generated. If your mod places blocks **after**
generation - a schematic paste, a structure stamp, a room builder - the engine does not
recalculate light for them in a custom dimension, and the vanilla `/debug chunk relight` command
is dimension-blind. Request a dim-aware relight explicitly:

```csharp
var manifold = sapi.GetManifoldServer(this);
manifold.RelightRegion(
    new AssetLocation("mymod", "pocket"),
    new BlockPos(0, 60, 0, 0),      // min corner (dimension field is overwritten)
    new BlockPos(47, 80, 47, 0));   // max corner
```

The call is synchronous and best-effort; its cost scales with the relit volume, so keep the region
bounded to what you actually changed. Throws `DimensionNotFoundException` for unknown codes and
`ManifoldUnhealthyException` if Manifold failed to initialize.

### `/manifold relight` admin command

For in-game diagnosis, server admins (privilege `controlserver`) can run:

```
/manifold relight [radius]
```

It relights the chunk columns around the caller in the dimension they are standing in, over the
full world height. `radius` is in chunks (default 1, max 4). Use it to confirm whether a lighting
glitch is a stale-light problem (the command fixes it) or something else (it does not).

### Load radius and render distance

The effective streaming radius is `max(loadRadius, server view distance)`, so generated terrain
always reaches at least as far as players can see, regardless of the configured `loadRadius`.
