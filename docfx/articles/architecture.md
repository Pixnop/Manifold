# Architecture

This document gives an overview of Manifold's internal structure: how the components relate, the server/client split, and why no Harmony patches are used.

## Entry Point: ManifoldModSystem

`ManifoldModSystem` is the VS `ModSystem` that wires everything together. It runs at load order `0.05` so it starts before consumer mods (which should use order `0.5` or later).

On `StartServerSide` it instantiates the internal services, wires them together, and registers the `manifold:overworld` built-in dimension. On `StartClientSide` it initializes the client mirror.

## Internal Services (Server Side)

```
ManifoldModSystem
  ├── DimensionAllocator        - assigns and reclaims VS engine dimension ids (0-1023)
  ├── DimensionRegistry         - the source of truth; owns IDimension objects and fires events
  ├── DimensionPersistence      - reads/writes the dimension manifest from the savegame, classifies entries at boot
  ├── DimensionGenerator        - active bounded-region generation: creates/loads columns, invokes IWorldgenStrategy
  ├── StreamingWorldgenDriver   - per-tick streaming generation for .Streaming(...) dimensions
  ├── TransitService            - TeleportPlayer/Entity/Block logic, event dispatch, game-mode and inventory policy
  ├── GeneratedColumnStore      - persisted set of already-generated columns (load vs. regenerate)
  ├── PlayerPositionStore       - persisted per-player, per-dimension last-visited positions
  ├── ManifoldNetworkChannel    - serialises the dimension list and pushes updates to clients
  └── HarmonyPatcher            - runs PatchAll (currently zero patches) as a boot health gate; see Zero Harmony below
```

Each service is an internal class in the `Manifold.Internal` namespace (`HarmonyPatcher` lives in the
`Manifold.Internal.HarmonyPatches` sub-namespace). Consumers never reference these types directly - they
interact only through the `Manifold.Api` interfaces.

## Facades

`IManifoldServer` is the server-side facade returned by `sapi.GetManifoldServer(...)`. It exposes
`Registry`, `Transitions`, `IsHealthy`, `RelightRegion` (dim-aware relight for blocks placed after
generation) and `ForceRemoveDimension` (evacuate-then-remove for an occupied Ephemeral dimension).

`GetManifoldServer(this)` (with a `ModSystem` argument) wraps the shared facade in an `OwnerScopedManifoldServer` that tags any dimension registered through it with the caller's mod id. This is how Manifold tracks dimension ownership for quarantine.

`IManifoldClient` is the client-side facade returned by `capi.GetManifoldClient()`. It provides a read-only snapshot of the dimension list (replicated from the server, metadata included), `GetDimensionOf(entity)` to locate an entity by its mirrored dimension, and `LocalPlayerChangedDimension`, raised on the main thread after the local player transits.

## Sided Split

| Concern | Server | Client |
|---------|--------|--------|
| Registry mutations | `IDimensionRegistry.Define` / `Create` / `TryRemove` | Read-only mirror |
| Transit | `ITransitionService.TeleportPlayer` / `TryTeleportPlayer` / `TeleportEntity` / `TeleportBlock` | Not applicable |
| Worldgen | `IWorldgenStrategy` called by `DimensionGenerator` (and `StreamingWorldgenDriver` for streaming dims) | Not applicable |
| Dimension list | Authoritative | Replicated via `ManifoldNetworkChannel` |

Consumers should guard server-only code in `StartServerSide` and client-only code in `StartClientSide`.

## Persistence

`ManifoldModSystem.StartServerSide` reads the manifest from the savegame and seeds each entry right
away, before any consumer mod's `StartServerSide` runs: `Quarantined` if the entry's owner mod is not
in the loaded mod list, otherwise `Pending`. There is no later promotion pass - a `Pending` entry
becomes `Active` only when its owner calls `Define(code)...RegisterStatic()`/`Create()` again with the
same code; if the owner never does, it stays `Pending` for the rest of the session.

Generated-column sets are also persisted: if the same column is requested again after a restart, Manifold skips the `IWorldgenStrategy.GenerateColumn` call and trusts the saved chunk data.

Per-player last-visited positions are keyed by `(playerUid, engine dimension id)` and stored under
their own savegame key (`manifold:lastpos`), separate from the manifest (`manifold:manifest`) and the
generated-column set (`manifold:genchunks`).

## Zero Harmony

Manifold does not patch any engine method. All behaviour is implemented through public VS API surface:

- Worldgen: `IWorldManagerAPI.CreateChunkColumnForDimension` / `LoadChunkColumnForDimension`, a bulk
  block accessor (`GetBlockAccessorBulkUpdate`), and `ForceSendChunkColumn` - driven directly by
  `DimensionGenerator`, not by the `ChunkColumnGeneration` event hook.
- Transit: `EntityPlayer.ChangeDimension` (rebinds chunk membership and fires the engine's own
  `PlayerDimensionChanged` event) followed by `TeleportToDouble`, plus dimension-encoded `BlockPos`.
- Networking: VS's built-in `IServerNetworkChannel` / `IClientNetworkChannel`.
- Block registration: `ICoreAPI.RegisterBlockClass` (for optional portal blocks).

The only non-public access in Manifold is `EntityPosAccess`: a reflection-built accessor that reads
`Entity.Pos` as either a field or a property, since VS 1.22 changed its shape from one to the other and
Manifold ships one binary that must work against both.

`0Harmony` is still referenced: `HarmonyPatcher` runs `PatchAll` with zero `[HarmonyPatch]`-annotated
classes in the assembly today, purely as a boot health check (forward-compatible if a patch is ever
added). If Harmony fails to load, `HarmonyPatcher.IsHealthy` is `false` and `IManifoldServer.IsHealthy`
reports `false` too. Dimension registration still succeeds in that state (on a disconnected registry
with no in-game effect), but `TeleportPlayer`/`TeleportEntity`/`TeleportBlock`, `RelightRegion`,
`GenerateRegion`, and `ForceRemoveDimension` all throw `ManifoldUnhealthyException` instead of
silently doing nothing.

## Package Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| `VintagestoryAPI` | Provided by the game at `$VINTAGE_STORY` | Core VS types |
| `0Harmony` | Bundled with VS | Referenced at boot: `PatchAll` runs with zero patches, purely as the health check described above |
| `protobuf-net` | Bundled with VS | Network message serialization |

No NuGet packages beyond the test tooling are introduced by Manifold.
