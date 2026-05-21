# Architecture

This document gives an overview of Manifold's internal structure: how the components relate, the server/client split, and why no Harmony patches are used.

## Entry Point: ManifoldModSystem

`ManifoldModSystem` is the VS `ModSystem` that wires everything together. It runs at load order `0.05` so it starts before consumer mods (which should use order `0.5` or later).

On `StartServerSide` it instantiates the internal services, wires them together, and registers the `manifold:overworld` built-in dimension. On `StartClientSide` it initializes the client mirror.

## Internal Services (Server Side)

```
ManifoldModSystem
  ├── DimensionAllocator      - assigns and reclaims VS engine dimension ids (0-1023)
  ├── DimensionRegistry       - the source of truth; owns IDimension objects and fires events
  ├── DimensionPersistence    - reads/writes the dimension manifest + player positions from the savegame
  ├── WorldgenDispatcher      - drives active bounded-region generation per transit
  ├── TransitService          - TeleportPlayer logic, event dispatch, game-mode enforcement
  └── NetworkingService       - serialises the dimension list and pushes updates to clients
```

Each service is an internal class in the `Manifold.Internal` namespace. Consumers never reference these types directly - they interact only through the `Manifold.Api` interfaces.

## Facades

`IManifoldServer` is the server-side facade returned by `sapi.GetManifoldServer(...)`. It exposes `Registry`, `Transitions`, and `IsHealthy`.

`GetManifoldServer(this)` (with a `ModSystem` argument) wraps the shared facade in an `OwnerScopedManifoldServer` that tags any dimension registered through it with the caller's mod id. This is how Manifold tracks dimension ownership for quarantine.

`IManifoldClient` is the client-side facade returned by `capi.GetManifoldClient()`. It provides a read-only snapshot of the dimension list (replicated from the server).

## Sided Split

| Concern | Server | Client |
|---------|--------|--------|
| Registry mutations | `IDimensionRegistry.Define` / `Create` / `TryRemove` | Read-only mirror |
| Transit | `ITransitionService.TeleportPlayer` | Not applicable |
| Worldgen | `IWorldgenStrategy` called by `WorldgenDispatcher` | Not applicable |
| Dimension list | Authoritative | Replicated via `NetworkingService` |

Consumers should guard server-only code in `StartServerSide` and client-only code in `StartClientSide`.

## Persistence

On server start, `DimensionPersistence` reads the manifest from the savegame's mod data. Dimensions found there but not re-registered by any loaded mod become `Pending`. After `StartServerSide` of all mods has run, any still-`Pending` dimensions whose owner mod is absent are promoted to `Quarantined`.

Generated-column sets are also persisted: if the same column is requested again after a restart, Manifold skips the `IWorldgenStrategy.GenerateColumn` call and trusts the saved chunk data.

Per-player last-visited positions are stored keyed by `(playerUid, dimensionCode)` in the same savegame entry.

## Zero Harmony

Manifold does not use Harmony patches. All behaviour is implemented through public VS API surface:

- Worldgen: `IServerAPI.WorldManager.CreateChunkColumnForDimension` and the `ChunkColumnGeneration` event listener.
- Transit: `IServerPlayer.Entity.TeleportTo` and dimension-encoded `BlockPos`.
- Networking: VS's built-in `IServerNetworkChannel` / `IClientNetworkChannel`.
- Block registration: `ICoreAPI.RegisterBlockClass` (for optional portal blocks).

One internal implementation detail: reading the dimension id from a chunk during the `ChunkColumnGeneration` event uses a single reflection read, since that field is not yet exposed in the public API. This is the only non-public access in Manifold.

## Package Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| `VintagestoryAPI` | Provided by the game at `$VINTAGE_STORY` | Core VS types |
| `0Harmony` | Bundled with VS | Listed as available but not used |
| `protobuf-net` | Bundled with VS | Network message serialization |

No NuGet packages beyond the test tooling are introduced by Manifold.
