# Architecture

<picture>
<source media="(prefers-reduced-motion: reduce)" srcset="../assets/site/header-architecture.png">
<img class="mf-article-header" src="../assets/site/header-architecture.webp" width="220" height="160" alt="Three islands linked by transit arcs" loading="lazy" decoding="async">
</picture>

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
  ├── SchemaSidecar             - schema version recorded per persisted blob (see Schema versioning)
  └── ManifoldNetworkChannel    - serialises the dimension list and pushes updates to clients
```

Each service is an internal class in the `Manifold.Internal` namespace. Consumers never reference
these types directly - they interact only through the `Manifold.Api` interfaces.

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

### Schema versioning

Every blob Manifold persists (the dimension manifest, the generated-column set, per-player
last-visited positions, the per-player inventory-separation profile, `manifold:inv` in player
moddata, and the saved pre-forced game mode, `manifold:gamemode-before-forced`, also in player
moddata) carries an explicit schema version, without changing that blob's own bytes at all: a world
saved by this version still opens correctly with Manifold 0.5.x, and a later release that changes a
format is refused by this one instead of misread.

The version lives in a separate sidecar record, `SchemaSidecar`, kept next to the blobs it
describes: one in the savegame under `manifold:schema` for the three savegame-level blobs, and one
in each player's moddata under the same key for that player's two blobs. It is encoded as a
`TreeAttribute` mapping blob key to version (int), the same structure the manifest itself already
uses for its own entries, so there is no new serialization format to write or maintain. A blob key
absent from the sidecar, including an absent sidecar altogether (every world saved before this
sidecar existed), means version 1.

Reading a blob whose sidecar-recorded version this build does not recognize (greater than the
version it writes) refuses that blob instead of misreading it: it is logged (naming the key and
both versions), the raw bytes are copied to a `<key>.unrecognized` recovery key so they are never
lost, and the refusal is latched for the session (`DimensionPersistence`, `GeneratedColumnStore`
and `PlayerPositionStore` each expose `IsVersionRefused`), and `ManifoldModSystem` skips re-saving a
refused key's blob for the rest of the session so it is never overwritten by this session's
incomplete in-memory state (an empty set for the generated-column store or the position store with
no consumer re-registered yet, or a partial manifest). An unrecognized version with no data behind
it (an absent or empty blob) is not refused: it is read as empty, the same as an absent blob at a
recognized version, since there is nothing there to protect from being overwritten.
`GeneratedColumnStore` additionally treats every column as already generated while refused, so
`DimensionGenerator` loads columns from disk instead of regenerating over them without a reliable
record of what the newer version already created. The saved pre-forced game mode and the per-player
inventory profile already avoided the equivalent overwrite problem by construction: the restore and
the swap are simply skipped on refusal, leaving the raw moddata as it was. For the saved game mode
specifically, this means a player who left a forced dimension while its blob was refused keeps the
forced mode in every dimension, including unforced ones, until a build that recognizes the blob's
version runs; the blob, sidecar entry and recovery copy are all left untouched, so no data is lost,
only the restore is deferred. In every case, a refused key's sidecar entry is left exactly as read:
never downgraded back to a version this build would then treat as safe to write over.

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

Manifold does not reference Harmony at all. `IManifoldServer.IsHealthy` and `IManifoldClient.IsHealthy`
are always `true` and `ManifoldUnhealthyException` is never thrown; both are kept only for binary
compatibility with mods compiled against earlier versions.

## Package Dependencies

| Package | Source | Purpose |
|---------|--------|---------|
| `VintagestoryAPI` | Provided by the game at `$VINTAGE_STORY` | Core VS types |
| `protobuf-net` | Bundled with VS | Network message serialization |

No NuGet packages beyond the test tooling are introduced by Manifold.
