<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="../assets/site/header-transit-and-travel-policy.png">
<img class="mf-article-header" src="../assets/site/header-transit-and-travel-policy.webp" width="220" height="160" alt="A chest carried through a portal between two islands" loading="eager" fetchpriority="high" decoding="async">
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

### What you can rely on when it returns

The transit is **synchronous**: it runs entirely on the calling main thread. You do not need to wait
for `PlayerEntered` to know what happened, because by the time `TryTeleportPlayer` returns:

- `true`: the player entity has been rebound to the target dimension (its `Pos.Dimension` is already
  the target's id), the engine teleport to the landing position has been requested, the target's
  game-mode and inventory policies have been applied, and `PlayerLeft` then `PlayerEntered` have
  been raised, with every subscriber already run.
- `false`: the player was not moved, and neither `PlayerLeft` nor `PlayerEntered` was raised. A cancel
  at `PlayerArriving` or a refused dismount happens after the destination region was generated, so
  that region may exist and the player's position in the source dimension is already recorded for
  `LastVisited`, but the player has not moved.

**Not guaranteed on return: the entity's X/Y/Z.** The engine applies the landing coordinates from a
callback that is queued when the dimension 0 chunk column at the landing X/Z is not loaded (typical
with `WithFixedSpawn`, `LastVisited` or `OverridePosition` far from where the player stands), so
they can arrive a few ticks later and the entity may still hold its source coordinates when
`PlayerEntered` fires. Use `PlayerEnteredDimensionEventArgs.TargetPosition` for the landing
position instead of reading the entity.

`TeleportPlayer` has the same ordering without the return value. Two further things are outside the
guarantee: a client-side mod hears about the transit through a network packet and so sees
`LocalPlayerChangedDimension` slightly later, and an unknown or inactive target throws before
anything happens.

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
| `PlayerEntered` | After the player has entered the target dimension. | No | Player, source, target, landing position (`TargetPosition`), requested arrival yaw (`Yaw`, 0.6.1). |
| `EntityChangedDimension` (0.4.0) | After `TeleportEntity` re-homes a non-player entity. | No | Entity, previous and new `IDimension`, final position. |

`PlayerEntering` is the right hook for veto logic (blocked players, missing prerequisites).
`PlayerArriving` is the right hook for setup work that needs the destination chunks already loaded
(place a welcome block, attach server-side state, log arrival metadata) - it can still cancel the
transit. `PlayerLeft` / `PlayerEntered` are post-teleport; use them for cleanup and state propagation.

`PlayerLeft` and `PlayerEntered` are also raised when a player who died in a dimension respawns out
of it (`PlayerEntering` and `PlayerArriving` are not, since a respawn cannot be refused): see
[Death and respawn](#death-and-respawn).

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
| `Yaw` (0.6.1) | The yaw, in radians, the player faces on arrival. `null` (the default) keeps their current yaw. Players only: `TeleportEntity` ignores it. See [Arrival yaw](#arrival-yaw). |

```csharp
// Transit to a specific absolute position. The dimension field (the 4th argument) is
// overwritten with the target's internal id, so 0 here is fine.
transitions.TeleportPlayer(player, new AssetLocation("mymod", "arena"), new TransitionOptions
{
    OverridePosition = new BlockPos(512, 70, 512, 0)
});
```

## Arrival yaw

Set `TransitionOptions.Yaw` to make the player face a given way when they land, in radians, the same
unit as `EntityPos.Yaw`. Leave it `null` and they keep the yaw they had.

```csharp
transitions.TeleportPlayer(player, new AssetLocation("mymod", "arena"), new TransitionOptions
{
    Yaw = MathF.PI / 2,
});
```

Manifold applies it in two halves. It tells the player's own client to turn the camera when the
transit completes (`PlayerEntered` time), and it sets the entity's yaw once the engine has actually
moved the player. When the engine has to wait for the destination to load, that move comes some ticks
later, so the camera turns first and the position follows. Both halves matter: a player's camera is
driven by their client, so changing the server-side entity yaw alone (which is all `TeleportToDouble`
followed by `Entity.Pos.Yaw = ...` does) never reaches the screen, and a client would also send its
old orientation straight back. `PlayerEntered` reports the requested yaw as `e.Yaw` (`null` when none
was asked for).

## Returning a player to where they came from

Every time a player transits into a different dimension through Manifold, it records their origin for
that dimension: the dimension they left, the exact position (doubles, not block coordinates) and the
yaw they were facing. It is saved with the world, so it survives logout and server restarts.

```csharp
TransitOrigin? GetOrigin(IServerPlayer player);   // for the dimension the player is in now
bool TryReturnPlayer(IServerPlayer player);
```

`GetOrigin` returns `null` when nothing is recorded for the player's current dimension, or when the
dimension they came from no longer exists. `TryReturnPlayer` sends the player back there: that
dimension, that exact position (no surface search, no spawn behavior) and that yaw. It is an
ordinary transit, so the same events fire (`PlayerEntering`, `PlayerArriving`, `PlayerLeft`,
`PlayerEntered`), a subscriber can cancel it, a rider is dismounted, and the game mode and inventory
policies of the destination apply.

```csharp
// "Leave" button inside a mod's dimension: back to wherever the player entered from.
if (!transitions.TryReturnPlayer(player))
{
    // Nothing recorded, the origin dimension is gone or inactive, or a subscriber vetoed it.
    transitions.TeleportPlayer(player, new AssetLocation("manifold", "overworld"));
}
```

`TryReturnPlayer` returns `true` when the transit went through. The engine may apply the position some
ticks later, so do not read the entity's coordinates as final straight away. It returns `false`, and logs which case it was at Notification level, when:

- nothing is recorded for the dimension the player is in;
- the origin dimension no longer exists, or is not `Active` (an ephemeral dimension that was reaped,
  or a persistent one whose owner mod has not re-claimed it yet);
- a `PlayerEntering` or `PlayerArriving` subscriber cancelled the transit, or the player's mount
  refused to release them.

A return does not record a new origin for the dimension it lands in, so chains unwind one step per
call: after overworld, then A, then B, a return from B lands in A, and a return from A lands in the
overworld, instead of bouncing between A and B. Any other transit (including a transit to a
dimension the player has already visited) replaces the origin recorded for its destination. A
transit within the same dimension records nothing.

The engine applies a teleport once the destination column is loaded, which can be some ticks after
the call. A player who transits again in the meantime still has the coordinates they left on their
entity, so Manifold records the landing the earlier transit asked for as their position instead: the
origin is where the player was heading, not the coordinates they left. A player who disconnects
while such a teleport is waiting is recorded the same way.

For the same reason, a transit that is overtaken before the engine applied it (step into a dimension
whose terrain is far away, then straight back out with `TryReturnPlayer`) does not drag the player
away when the engine finally gets to it: Manifold sends them back to the landing of the transit that
overtook it, and the overtaken transit's yaw is not applied. The engine moves the player once more
in that case, so a client may see the player at the overtaken landing for a moment before the
correction.

Origins are stored by dimension id plus the codes of both dimensions, and dropped when either end is
removed. Ephemeral dimension ids are recycled, so an origin whose id now belongs to another dimension
is ignored (it reads as "nothing recorded") instead of being followed.

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

## Death and respawn

When a player dies and presses Respawn, the game picks a spawn position (the temporal gear they used,
else the world spawn, which can be a random spot within the world's spawn radius) and teleports them
there with X, Y and Z only. It never changes the dimension. Left alone, a player who died in a custom
dimension would come back in that dimension at the overworld spawn's coordinates: in a void dimension
that is empty air and a possible death loop, in a solid one it can be inside rock.

Manifold takes the player out. By default (`RespawnBehavior.Overworld`) they respawn in the overworld
at the position the game chose, as the game means a respawn. The move goes through the same machinery
as any other way out of a dimension:

- The game mode is handed back and the inventory profile is swapped back, exactly as when walking out.
- `PlayerLeft` and `PlayerEntered` are raised, and `PlayerEntered.TargetPosition` is the landing block
  (`Yaw` is `null`: the game does not turn a respawning player, and neither does Manifold). The client
  gets Manifold's usual transit notification, and an ephemeral dimension left empty is reaped.
- `PlayerEntering` and `PlayerArriving` are not raised. A respawn cannot be refused, and a veto would
  leave the player stuck in the dimension they died in.
- No origin is recorded, so `TryReturnPlayer` cannot send a respawned player back to where they died,
  and the dimension's last-visited position is left as it was: the respawn coordinates are not a place
  the player walked to, and recording them would drop a `LastVisited` player into the void next time.

The engine's own death handling is untouched. With a separate inventory (`WithSeparateInventory`),
whatever the game does with the items at death happens first: by default the hotbar and backpack are
dropped where the player fell, in the dimension, and with the world's keep-inventory penalty they stay
on the player. Manifold then swaps to the overworld's set as on any exit, so nothing is duplicated and
nothing is lost: kept items wait in the dimension's own set for the next visit, dropped ones are on the
ground in the dimension.

The move happens once the game has revived the player. When the spawn column is loaded that is within
the respawn request itself; otherwise the game waits for the column and Manifold follows a few ticks
later. A death in the overworld is never touched, a respawn request from a player who is alive is
ignored (the game ignores it too), and a player who disconnects while dead is handled when they come
back. If their dimension no longer exists by then, the join rescue has already taken them to the
overworld and the respawn is an ordinary overworld one.

### A spawn point inside a dimension

The temporal gear stores the player's position through the engine's dimension-aware Y (the Y plus
32768 times the dimension id). Used inside a custom dimension, it gives the game a spawn whose Y
carries that dimension, and the game's respawn does not decode it: the player would stand at a Y far
above the dimension. Manifold decodes it. The player respawns in the dimension the spawn designates,
under that dimension's own policies, or at the world's default spawn in the overworld when that
dimension is gone or not active.

### Keeping the dead inside

A dimension that wants its players back where they were (an arena, a hub) opts in:

```csharp
manifold.Registry
    .Define(new AssetLocation("mymod", "arena"))
    .Persistent()
    .WithWorldgen(new BasicVoidWorldgenStrategy())
    .WithFixedSpawn(new BlockPos(0, 64, 0, 0))
    .WithRespawnBehavior(RespawnBehavior.DimensionSpawn)
    .RegisterStatic();
```

The player respawns inside the dimension at its fixed spawn, wherever the game would have put them
(a spawn from a temporal gear included). This is a move within the dimension and nothing else: no
event is raised and no policy changes. Without `WithFixedSpawn` the option has nothing to land on:
the player respawns in the overworld and Manifold logs a warning once per dimension. A quarantined
dimension is never kept: its dead respawn in the overworld.

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
