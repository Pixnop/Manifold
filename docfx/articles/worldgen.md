<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="../assets/site/header-worldgen.png">
<img class="mf-article-header" src="../assets/site/header-worldgen.webp" width="220" height="160" alt="A chunk of terrain building itself, block by block" loading="lazy" decoding="async">
</picture>

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

Called once, on the first generation for the dimension - a player transit or an `IManifoldServer.GenerateRegion` call (lazy initialization). `IWorldgenInitContext` gives you `Api` (the `ICoreServerAPI`), plus `DimensionId` and `Seed`; use `ctx.Api.World.GetBlock(...)` to resolve block ids. Store them in fields for later use in `GenerateColumn`.

```csharp
public sealed class MyFloorStrategy : IWorldgenStrategy
{
    private int _stoneBlockId;

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        // GetBlock returns null for an unknown code; fall back to id 0 (air) rather than crash.
        _stoneBlockId = ctx.Api.World.GetBlock(new AssetLocation("game", "rock-granite"))?.Id ?? 0;
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

The seed is `worldSeed ^ (chunkX * 1299721) ^ (chunkZ * 1000033)` - derived from the world seed and the
column's own coordinates, not the dimension. Two dimensions generating the same column coordinates get
the same seed and therefore the same first `Rng` draw; if your strategy needs dimension-distinct
output at identical coordinates, fold `ctx.DimensionId` into your own derived seed inside
`GenerateColumn`.

> **Important:** Always dimension-encode positions. A `BlockPos` without `DimensionId` defaults to dimension 0 (the overworld) and will silently write to the wrong world.

## Active Bounded-Region Generation

When a player transits into a dimension for the first time (or after a server restart for a Persistent dimension), Manifold:

1. Computes the target chunk column from the transit destination position.
2. Iterates all columns within `generationRadius` chunks in X and Z, skipping any column whose chunk X or Z is negative (see [WithGenerationRadius](#withgenerationradius)).
3. For each remaining column: calls `CreateChunkColumnForDimension`, then `strategy.GenerateColumn`, then sends the column to the client. There is no relight pass (see [Lighting](#lighting-in-custom-dimensions)).
4. Completes the player teleport once generation finishes.

This happens **synchronously on the main thread** before the player arrives - so the player never sees an ungenerated void.

> **Registration alone creates no terrain.** `RegisterStatic()` and `Create()` only record the
> dimension; `WithFixedSpawn` and `WithGenerationRadius` describe what to generate, not when. Until
> something generates it - a player transit, a rejoining player, or a `.Streaming(...)` driver -
> every position reads as air. If your mod needs content to exist before the first arrival - a spawn
> platform, a prebuilt hub - call `IManifoldServer.GenerateRegion(dimension, center)` yourself right
> after registration; it runs the same bounded generation a transit would, synchronously, with no
> player involved. See [The Registry](dimensions.md#the-registry) for an example.

## Post-processing a Generated Column

`IDimensionRegistry.ColumnGenerated` fires right after a column is generated, not when an
existing one is only loaded (a restart, or a re-visit). Use it to decorate the terrain a strategy
just produced without touching the strategy itself: a structure, a marker, loot (from any mod,
not only the one that owns the dimension):

```csharp
manifold.Registry.ColumnGenerated += (_, e) =>
{
    if (e.Dimension.Code.Path != "mydim")
    {
        return;
    }

    var pos = new BlockPos((e.ChunkX * 32) + 16, 20, (e.ChunkZ * 32) + 16, e.Dimension.InternalId);
    e.BlockAccessor.SetBlock(myMarkerBlockId, pos);
};
```

`e.BlockAccessor` is a plain accessor: writes apply immediately, no `Commit()` needed. It uses the
same `synchronize:false, relight:false` semantics as worldgen itself, so a write here does not
queue a server relight task or a neighbour-update/resync entry the way a live player edit would;
call `IManifoldServer.RelightRegion` afterwards if the decoration needs lighting. The column has
not been sent to any client when this event fires (sending always happens right after), so a
block placed here is included in that first send with no separate resync. Only the event's own
column is guaranteed loaded: a write that lands in a neighbour column not generated yet is
silently dropped, so split a structure that spans multiple columns and place it per column. Raised
on the server main thread, for every generation path: a transit, the streaming driver, and
`IManifoldServer.GenerateRegion`. A throwing subscriber is isolated like `Created`/`Destroyed`.

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
- Chunk columns with a negative X or Z do not exist in Vintage Story and are always skipped, by both
  bounded generation and streaming. A region centered near world coordinates (0, 0) is clipped on the
  negative sides. Put spawns and content well inside positive coordinates - the sample uses
  (1024, 64, 1024) - to get the full, unclipped region.

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
| Region size | Fixed `(2r+1) x (2r+1)` columns (clipped where that would cross a negative X or Z) | Follows each player continuously (same clipping near negative X or Z) |
| Walking past the edge | Ungenerated (void) | No edge away from the world's negative corner - new chunks stream in |
| Best for | Dungeons, arenas, lobby spaces | Open-world or exploration dimensions |

The two modes coexist. A dimension can use only bounded generation, only streaming, or both (bounded for the initial landing pad, streaming for ongoing movement).

## Lighting in custom dimensions

Manifold does not relight a column after generating it. The automatic relight pass was removed in
0.4.2: a dimension-aware full relight floods a custom dimension with maximum skylight (the engine
seeds skylight from the top of each column with no per-dimension day/night gate, see
[issue #62](https://github.com/Pixnop/Manifold/issues/62)), and the synchronous pass stalled the
first visit to a bounded dimension. Custom dimensions therefore use the engine's native client-side
lighting, as they did in every release before the relight experiment.

What that means in practice:

- **Open or mostly-air dimensions render fully lit**, whatever the time of day. Use
  `WithDarkSky(ceilingY)` when you want a dark dimension: it seals every generated column with an
  opaque ceiling so the area below is lit only by block light (torches, lamps, lava). Pair it with
  `WithFixedSpawn(...)` at a Y below `ceilingY`. The default `SameCoordinates` resolver scans downward
  from the world's height looking for the first non-liquid block with two clear blocks above it (see
  [the default surface search](transit-and-travel-policy.md#the-default-surface-search)); with no
  fixed spawn (or on a first `LastVisited` visit) it finds the ceiling cap first, unless `ceilingY` is
  within two blocks of the world's top, in which case it skips the cap for lack of headroom and lands
  the player inside the dark space below instead.
- **Solid-filled dimensions** (terrain carved into rooms) are dark without any option.
- **Blocks placed after generation** are not relit by the engine in a custom dimension; use
  `RelightRegion` or `/manifold relight`, described below.
- **Per-dimension day/night** does not exist: the engine keeps a single global calendar and sky
  light uniform. Per-dimension time of day is tracked in
  [issue #55](https://github.com/Pixnop/Manifold/issues/55).

`WithRelightHeight(maxY)` has had no effect since 0.4.2 and is marked `[Obsolete]`; it still
validates its argument so existing callers keep working.

### Relighting at runtime (`RelightRegion`)

If your mod places blocks **after**
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
bounded to what you actually changed. Throws `DimensionNotFoundException` for unknown codes.

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
