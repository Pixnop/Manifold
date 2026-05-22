# Transit and Travel Policy

Manifold provides a single primitive for moving players between dimensions - `ITransitionService.TeleportPlayer` - plus helpers that layer on top of it. Travel policy (where the player lands and what game mode they use) is configured per-dimension on the builder and optionally overridden per-transit.

## TeleportPlayer

```csharp
void TeleportPlayer(
    IServerPlayer player,
    AssetLocation targetDim,
    TransitionOptions options = default);
```

Must be called on the **main thread**. The method:

1. Raises `PlayerEntering` (cancellable - set `e.Cancel = true` to abort).
2. Resolves the landing position using the dimension's spawn behavior (or the `options` override).
3. Pre-generates terrain around the landing position if needed.
4. Teleports the player and adjusts their game mode if the dimension forces one.
5. Raises `PlayerLeft` (source dimension) and `PlayerEntered` (target dimension).

Throws `DimensionNotFoundException` if the code is unknown, `DimensionStateException` if the dimension is not `Active`, or `ManifoldUnhealthyException` if Manifold failed to initialize.

```csharp
var transitions = manifold.Transitions;

transitions.PlayerEntering += (_, e) =>
{
    if (IsPlayerBanned(e.Player))
        e.Cancel = true;
};

transitions.TeleportPlayer(player, new AssetLocation("mymod", "nether"));
```

## TeleportEntity

`TeleportEntity` moves a non-player entity (a dropped item, a creature) between dimensions. It mirrors
`TeleportPlayer`: the destination region is generated on demand, then the entity is re-homed into it.

```csharp
// e.g. send a dropped item entity to another dimension
transitions.TeleportEntity(itemEntity, new AssetLocation("mymod", "nether"));
```

- It throws `ArgumentException` if given a player entity - use `TeleportPlayer` for players.
- The landing position comes from `TransitionOptions.OverridePosition` or the resolver (default
  `SameXZSurfaceY`); the per-dimension `SpawnBehavior`/`LastVisited` policy is player-only and is not
  applied to entities.
- Item entities are fully supported. Other entities (mobs) are supported on a best-effort basis;
  verify AI and rendering behavior in your target dimension.

## TransitionOptions

`TransitionOptions` is an immutable record struct used to override per-transit behavior. All fields are optional:

| Property | Description |
|----------|-------------|
| `OverridePosition` | Hard-coded landing `BlockPos` (dimension-encoded). Skips all resolver logic. |
| `Resolver` | Custom `ITargetPositionResolver` - used when `OverridePosition` is null. |
| `SpawnBehavior` | Per-transit override of the dimension's configured spawn behavior. |
| `PreserveInventory` | Keep the player's inventory across transit (default: `true`). |

```csharp
// Transit to a specific absolute position.
transitions.TeleportPlayer(player, new AssetLocation("mymod", "arena"), new TransitionOptions
{
    OverridePosition = new BlockPos(512, 70, 512, arenaInternalId)
});
```

## SpawnBehavior

`SpawnBehavior` controls where a player lands when entering a dimension:

| Value | Effect |
|-------|--------|
| `SameCoordinates` | Keep the player's current X/Z; land on the surface at that column. Default. |
| `DimensionSpawn` | Always land at the dimension's configured fixed spawn point (set with `WithFixedSpawn`). |
| `LastVisited` | Return to where the player last was in this dimension; falls back to `SameCoordinates` on first visit. |

Configure it on the builder:

```csharp
// Fixed spawn.
manifold.Registry
    .Define(new AssetLocation("mymod", "lobby"))
    .Persistent()
    .WithWorldgen(new LobbyWorldgen())
    .WithFixedSpawn(new BlockPos(0, 64, 0, 0))    // sets DimensionSpawn implicitly
    .RegisterStatic();

// Remember last position.
manifold.Registry
    .Define(new AssetLocation("mymod", "survival"))
    .Persistent()
    .WithWorldgen(new SurvivalWorldgen())
    .WithSpawnBehavior(SpawnBehavior.LastVisited)
    .RegisterStatic();
```

Per-player last-visited positions are persisted in the savegame and survive server restarts.

## ITargetPositionResolver

For fully custom landing logic, implement `ITargetPositionResolver`:

```csharp
public interface ITargetPositionResolver
{
    // The entity is the player's entity for player transit, or the moved entity for entity transit.
    BlockPos Resolve(Entity entity, IDimension target, ICoreServerAPI api);
}
```

The resolver takes the source `Entity` (read its current X/Z from `entity.Pos`), so the same resolver
serves both `TeleportPlayer` and `TeleportEntity`. Pass it via `TransitionOptions.Resolver`. Built-in
resolvers are in `TargetPositionResolvers` (static factory class).

## WithForcedGameMode

You can force a game mode on all players entering a dimension:

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "creative-sandbox"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithForcedGameMode(EnumGameMode.Creative)
    .RegisterStatic();
```

The player's original game mode is not automatically restored when they leave - handle that in `PlayerLeft` if needed.

## WithSeparateInventory

A dimension can keep its own player inventory for chosen categories with the `[Flags]` enum
`ManifoldInventory` (`Hotbar`, `Backpack`, `Character`, or `All`):

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "vault"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithSeparateInventory(ManifoldInventory.Hotbar | ManifoldInventory.Backpack)
    .RegisterStatic();
```

On entering the dimension, the chosen categories are swapped to this dimension's set (empty on the
first visit); on leaving, the previous set is restored. Categories you do not list stay shared across
dimensions. Omit the call entirely for a fully shared inventory (the default).

How it stays safe:

- Each separated category is stored per "owner key" - the dimension code for a dimension that
  separates it, or `shared` for every dimension that does not. So all non-separating dimensions
  (including the overworld) share one set per category, and each separating dimension has its own.
- Profiles are saved in the player's moddata, alongside the physical inventory, so a snapshot and the
  live inventory are always written together. The current inventory is serialized before any slot is
  cleared, so a swap never loses items, and the profiles survive logout and server restarts.

## PortalBlockBase

`PortalBlockBase` is an abstract `Block` subclass that triggers a transit when a player collides with the block. Override `TargetDimensionCode` (and optionally `Options`):

```csharp
public sealed class NetherPortalBlock : PortalBlockBase
{
    protected override AssetLocation TargetDimensionCode =>
        new AssetLocation("mymod", "nether");

    protected override TransitionOptions Options => new TransitionOptions
    {
        SpawnBehavior = SpawnBehavior.LastVisited
    };
}
```

Register the block class in your mod's `Start`:

```csharp
public override void Start(ICoreAPI api)
{
    base.Start(api);
    api.RegisterBlockClass("NetherPortal", typeof(NetherPortalBlock));
}
```

Manifold does not register any portal block itself - portal block usage is entirely opt-in.

## DimensionCommandBuilder

`DimensionCommandBuilder` is a fluent builder for a VS chat command that calls `TeleportPlayer`:

```csharp
new DimensionCommandBuilder()
    .Command("nether")                                         // /nether
    .TargetDimension(new AssetLocation("mymod", "nether"))
    .RequiresPrivilege("chat")
    .WithSpawnBehavior(SpawnBehavior.LastVisited)
    .DescribedAs("Enter the Nether.")
    .Register(sapi);
```

The builder validates that `Command` and `TargetDimension` are set before registering. The resulting command is accessible via VS's standard `/help` output.
