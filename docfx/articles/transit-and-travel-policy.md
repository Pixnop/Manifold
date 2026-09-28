<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="../assets/site/header-transit-and-travel-policy.png">
<img class="mf-article-header" src="../assets/site/header-transit-and-travel-policy.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="lazy" decoding="async">
</picture>

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

1. Resolves a preliminary landing position using the dimension's spawn behavior (or the `options` override).
2. Raises `PlayerEntering` (cancellable, pre-generation - set `e.Cancel = true` to abort).
3. Pre-generates terrain around the landing position if needed.
4. Resolves the final landing position now that terrain exists.
5. Raises `PlayerArriving` (cancellable, post-generation, pre-teleport).
6. Teleports the player, then applies the target's game-mode policy (forces its mode, or restores the saved one) and swaps separated inventory categories.
7. Raises `PlayerLeft` (source dimension) and `PlayerEntered` (target dimension).

Throws `DimensionNotFoundException` if the code is unknown, or `DimensionStateException` if the dimension is not `Active`.

If the player is riding a mount (a boat, a saddled creature, any `IMountableSeat`), they are cleanly
dismounted right before the move. The mount is left behind in the source dimension: it is never
dragged along, and the player is never left flagged as mounted on an entity that never changed
dimension with them. A transit a subscriber cancels at `PlayerEntering`/`PlayerArriving` leaves the
player mounted, exactly as they were. If the mount's seat refuses to release them (for example a
moving elevator seat mid-move), the whole transit is aborted before anything moves, the same as a
cancellation; use `TryTeleportPlayer` to detect this.

```csharp
var transitions = manifold.Transitions;

transitions.PlayerEntering += (_, e) =>
{
    if (IsPlayerBanned(e.Player))
        e.Cancel = true;
};

transitions.TeleportPlayer(player, new AssetLocation("mymod", "nether"));
```

`TeleportPlayer` is `void`: a subscriber cancelling the transit at `PlayerEntering`/`PlayerArriving`
leaves the player where they were, silently as far as the return type is concerned. When you need to
know whether the transit actually happened, call `TryTeleportPlayer` instead - same behavior, same
exceptions, but it returns `true` if the player moved and `false` if a subscriber vetoed it:

```csharp
bool moved = transitions.TryTeleportPlayer(player, new AssetLocation("mymod", "nether"));
if (!moved)
{
    player.SendMessage(GlobalConstants.GeneralChatGroup, "Something stopped you from entering.", EnumChatType.Notification);
}
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

After a successful move, `ITransitionService.EntityChangedDimension` is raised with the entity, its
previous and new `IDimension`, and the final landing `BlockPos`. The event is the non-player
counterpart of the engine's `IEventAPI.PlayerDimensionChanged` and is not raised when `TeleportEntity`
throws.

## TeleportBlock

`TeleportBlock` (0.4.0) moves a single block, plus its `BlockEntity` state (inventory, attributes,
BE-behaviors), from one dimension to another. It completes the transit triplet alongside
`TeleportPlayer` and `TeleportEntity`.

```csharp
bool TeleportBlock(BlockPos source, AssetLocation targetDim, BlockPos targetLocal);
```

The destination region is generated on demand (same `EnsureRegion` path as `TeleportEntity`). The
source block is snapshotted via `BlockEntity.ToTreeAttributes`, written at the target, and rehydrated
via `FromTreeAttributes`; the source slot is then set to air. The target write happens before the
source clear, so an exception mid-flight leaves the block intact (the worst case is a duplicate,
never a lost block).

- The `targetLocal.dimension` field is overwritten with the target dimension's internal id; the
  caller's `BlockPos` is not mutated.
- Returns `true` when a non-air block was moved; `false` when the source slot was air (no-op), or
  when the source is refused (see below).
- The `BlockEntity`'s embedded position (`posx/posy/posz`, with `posy` dimension-encoded as
  `localY + dim * 32768`) is re-stamped to the target before rehydration so interactions (opening a
  chest, etc.) route correctly to the new position.

```csharp
// Move the looked-at block to a vault dimension.
var sel = serverPlayer.CurrentBlockSelection;
if (sel?.Position is { } src)
{
    if (transitions.IsMultiPositionBlock(src))
    {
        serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, "That block can't be moved on its own.", EnumChatType.Notification);
    }
    else
    {
        var target = new BlockPos(1024, 64, 1024, 0); // dimension overwritten by the service
        bool moved = transitions.TeleportBlock(src, new AssetLocation("mymod", "vault"), target);
    }
}
```

Throws the same `DimensionNotFoundException` / `DimensionStateException` as `TeleportPlayer`.

### Multi-position blocks are refused, not partially moved

A door, a bed, or any structure built on the engine's multiblock mechanism occupies more than one
grid position for one logical object. `TeleportBlock` only ever copies a single position plus its
`BlockEntity`, so moving one cell of such a structure would leave the rest of it behind, broken, at
the source, and drop an incomplete fragment of it at the target. Instead, `TeleportBlock` detects
this before writing anything and refuses the move: neither side is touched.

- A multiblock satellite or controller (the engine's `BlockMultiblock`, and anything built on
  `BlockBehaviorMultiblock`): this also covers ordinary doors, including wide gates, which fill
  every cell beyond their first with the same satellite mechanism (a plain 1-wide door is already
  two cells tall). A vanilla trapdoor is a single cell; it is not covered by this check and is moved
  normally.
- A large gear's fillers (`BlockMPMultiblockGear`) or its centre (`BlockLargeGear3m`).
- A bed's head or feet half.
- A large trough's head or feet half (`BlockTroughDoubleBlock`).
- A legacy door's up or down half (`BlockDoor`): worlds predating the current door behavior may
  still contain these.

The refusal returns `false` (the same value as the existing air no-op) and is logged as a
warning with the reason. Call `IsMultiPositionBlock` first to tell the two apart, or to give a
player a clearer message than a silent no-op:

```csharp
bool IsMultiPositionBlock(BlockPos pos);
```

## Transit events

`ITransitionService` exposes the full lifecycle as server-side events. Subscribe via the transitions
facade (`manifold.Transitions`):

| Event | When | Cancellable | Carries |
|-------|------|-------------|---------|
| `PlayerEntering` | Before any work, before generation. | Yes (`Cancel = true`) | Player, source, target, preliminary position. |
| `PlayerArriving` (0.4.0) | After region generation, before the teleport. | Yes | Player, source, target, final position. |
| `PlayerLeft` | After the player has left the source dimension. | No | Player, source, target. |
| `PlayerEntered` | After the player has entered the target dimension. | No | Player, source, target, landing position (`TargetPosition`). |
| `EntityChangedDimension` (0.4.0) | After `TeleportEntity` re-homes a non-player entity. | No | Entity, previous and new `IDimension`, final position. |

`PlayerEntering` is the right hook for veto logic (blocked players, missing prerequisites).
`PlayerArriving` is the right hook for setup work that needs the destination chunks already loaded
(place a welcome block, attach server-side state, log arrival metadata) - it can still cancel the
transit. `PlayerLeft` / `PlayerEntered` are post-teleport; use them for cleanup and state propagation.

In `PlayerEntered`, read `e.TargetPosition` rather than `e.Player.Entity.Pos`: the engine applies the
teleport only once the destination chunks arrive, so the entity can still report its source position.

```csharp
transitions.PlayerArriving += (_, e) =>
{
    // Place a welcome sign one block above the landing spot.
    Block? sign = sapi.World.GetBlock(new AssetLocation("game", "sign-ground-north"));
    if (sign is null)
    {
        return;
    }

    sapi.World.BlockAccessor.SetBlock(sign.Id, e.TargetPosition.UpCopy());
};

transitions.EntityChangedDimension += (_, e) =>
    sapi.Logger.Notification("[mymod] {0} arrived in {1}", e.Entity.Code, e.NewDimension.Code);
```

For players the engine also raises its own `IEventAPI.PlayerDimensionChanged`; Manifold's
`EntityChangedDimension` covers everything else.

## TransitionOptions

`TransitionOptions` is an immutable record struct used to override per-transit behavior. All fields are optional:

| Property | Description |
|----------|-------------|
| `OverridePosition` | Landing `BlockPos`. Skips all resolver logic. Its dimension field is stamped with the target's internal id on a copy, so the value you pass for it is ignored and your own instance is never mutated. |
| `Resolver` | Custom `ITargetPositionResolver` - used when `OverridePosition` is null. |
| `SpawnBehavior` | Per-transit override of the dimension's configured spawn behavior. |

```csharp
// Transit to a specific absolute position. The dimension field (the 4th argument) is
// overwritten with the target's internal id, so 0 here is fine.
transitions.TeleportPlayer(player, new AssetLocation("mymod", "arena"), new TransitionOptions
{
    OverridePosition = new BlockPos(512, 70, 512, 0)
});
```

## SpawnBehavior

`SpawnBehavior` controls where a player lands when entering a dimension:

| Value | Effect |
|-------|--------|
| `SameCoordinates` | Keep the player's current X/Z; land on the surface at that column (see below). Default. |
| `DimensionSpawn` | Always land at the dimension's configured fixed spawn point (set with `WithFixedSpawn`). |
| `LastVisited` | Return to where the player last was in this dimension; falls back to `SameCoordinates` on first visit. |

Configure it on the builder:

```csharp
// Fixed spawn.
manifold.Registry
    .Define(new AssetLocation("mymod", "lobby"))
    .Persistent()
    .WithWorldgen(new LobbyWorldgen())
    .WithFixedSpawn(new BlockPos(1024, 64, 1024, 0))    // sets DimensionSpawn implicitly
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

### The default surface search

`SameXZSurfaceY` (used by `SameCoordinates`, as the fallback for `DimensionSpawn`/`LastVisited` when
they have nothing to land on, and as `TeleportEntity`'s own default resolver) scans down from the
target dimension's ceiling and lands on the first solid, dry block that has two full blocks of clear
space above it (feet, then head). This means:

- A one-block gap in a cave ceiling is skipped (there is no room to stand in it), and the search
  keeps going for a spot with real clearance, above or below it.
- A plant, vine or torch with no floor to stand on is skipped too, the same as a one-block gap: the
  search walks through it like air instead of landing on top of it.
- The first liquid block found while scanning down is the top of that liquid body (a lake, an
  ocean): the search never continues past it looking for dry ground underneath, since real terrain
  never has open water floating over a cave, only the liquid's own bed and then solid ground. The
  liquid's surface is used as a landing spot, with the same two-block clearance check, only if the
  column has no dry spot above it.
- If the column has no valid spot whatsoever (a fully solid column, an empty/void column, one outside
  the world's height range, or a liquid body capped by solid ground with no clearance above it), the
  player's current Y is kept unchanged. This is the same behavior a void dimension always had; it now
  also covers a solid column with no opening in it.

Positions the caller chose explicitly are never second-guessed by this search: `OverridePosition`, a
custom `Resolver`, and a `DimensionSpawn`/`FixedSpawn` point are all used as-is.

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

`Resolve` is called twice per transit (once before generation, to center the pre-generated region on
a preliminary position; once after, for the final landing spot), so implementations must be
deterministic and side-effect free.

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

The mode the player had before entering the first forced dimension is saved in their player moddata
(so it survives logout and restarts) and restored when they transit to a dimension that forces
nothing. Chaining forced dimensions still returns the original mode, not the previous forced one.
Every transit path (commands, portals, `ForceRemoveDimension` evacuation, the join rescue) goes
through this - you do not need to handle it yourself in `PlayerLeft`.

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
