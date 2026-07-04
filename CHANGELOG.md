# Changelog

All notable changes to Manifold will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.2] - 2026-07-04

### Changed
- **Removed the automatic post-generation relight.** Earlier dev work made the worldgen relight dimension-aware (toward #59), but in-game testing showed that a dimension-aware `FullRelight` floods custom dimensions with full skylight - the engine seeds maximum skylight from the top of a dimension's column with no per-dimension day/night gate (an engine limitation, see #62) - and that the synchronous pass stalled the first visit to a bounded dimension. Manifold no longer relights on generation: custom dimensions use the engine's native client-side lighting, exactly as in every released version, and first-visit loads are fast again. Lighting is the engine's / consumer's concern. The opt-in manual relight tools below remain for mods that edit blocks after generation; `WithDarkSky` remains the supported way to get a reliably dark dimension.

### Added
- **Real-engine integration suite** (`tests/Manifold.Scenarios`, built on [Atlas](https://github.com/Pixnop/Atlas) 0.4.0) - 12 scenarios booting a headless Vintage Story server per test class and exercising Manifold against actual engine behavior: dimension registration and per-dimension worldgen isolation, entity/block/player transit, the separate-inventory swap (verified lossless across a three-leg round trip), the ephemeral lifecycle, and concurrent players. Runs in CI on every push (`e2e` job). Registration paths, the fixture-mod pattern and its rationale are documented in `tests/Manifold.Scenarios/README.md`.
- **`IDimensionBuilder.WithDarkSky(int ceilingY)`** - makes a dimension dark by sealing every generated column with an opaque ceiling at `ceilingY`. Vintage Story floods skylight downward from the top of a dimension's column with no per-dimension gate, so an open / mostly-air custom dimension renders fully lit regardless of time of day (an engine limitation, see #62); the opaque cap stops that flood so the area below stays dark and is lit only by block light (torches, lamps). Capping every generated column also makes those chunks non-empty, suppressing a client-side full-bright bleed from neighbouring empty chunks. Best for enclosed / underground dimensions; solid-filled dimensions are already dark without it. Verified in-game (dark interior, torches light it). Demonstrated by the sample's new `/darkdim`. Addresses the actionable half of #55 / #62.
- **`IManifoldServer.RelightRegion(dimension, min, max)`** - public dim-aware relight for mods that place blocks after generation (schematic paste, structure stamp, room builder). The engine's own relight paths and the vanilla `/debug chunk relight` command are dimension-blind; this is the supported way to recalculate light inside a custom dimension. Synchronous and best-effort; cost scales with the relit volume.
- **`/manifold relight [radius]`** admin command (privilege `controlserver`) - relights the chunk columns around the caller in their current dimension over the full world height. Radius in chunks, default 1, max 4. First subcommand of the new `/manifold` admin command.
- **`/manifold purge <code>`** admin command (privilege `controlserver`) - force-removes a dimension by code, evacuating any occupants to the overworld first, then releasing its engine id. This is the recovery path the docs and exception messages already pointed at for a Quarantined dimension (one whose owning mod was uninstalled) and the admin teardown for a Persistent one - both of which the ordinary `TryRemove` refuses. Without it, a Quarantined/Persistent dimension was permanently unreleasable and its engine id leaked from the 10..1023 pool across install/uninstall cycles.
- **A dimension is never destroyed while a player is inside it.** `IDimensionRegistry.TryRemove` now returns `false` if any connected player is standing in the dimension, instead of removing it out from under them. Move occupants out first, or use the force API below.
- **`IManifoldServer.ForceRemoveDimension(code)`** - deliberate teardown of an occupied ephemeral instance (session ended, arena closed): evacuates every occupant to the overworld (last-visited position), then removes the dimension. For BuiltIn/Persistent it throws like `TryRemove` without evacuating anyone.
- **Ephemeral dimensions are reaped automatically when emptied by a transit.** When a player *transits out* of an `Ephemeral` dimension and leaves it empty, it is removed (firing `Destroyed` for companion cleanup); it is also removed at server shutdown. Disconnecting does NOT reap it - a logged-out player keeps their dimension and reconnects straight back into it while the server stays up. Use `Persistent` for a dimension that must survive a restart.
- **Void rescue for stranded players.** A player whose saved position points to a dimension that no longer exists or is no longer `Active` (ephemeral gone after a restart, owning mod uninstalled, crash) is sent back to the overworld on join instead of loading into the void. The rescue teleport is deferred to `PlayerNowPlaying` so it never races the still-connecting client. Fixes #61.

### Removed
- **`TransitionOptions.PreserveInventory`** - the option was never read by the transit pipeline (inventory behavior is driven entirely by the destination dimension's `WithSeparateInventory` policy), and its name/default conflicted with that policy. The dead, misleading knob is removed rather than wired with confusing semantics.

### Fixed
- **A recycled engine id no longer loads the previous dimension's stale terrain.** When a dimension is destroyed, its generated-column markers are now pruned from `GeneratedColumnStore` (alongside the existing generator-state and last-position cleanup). Previously a destroyed dimension's column markers survived, so a new dimension allocated the same recycled engine id saw those columns as already generated and *loaded* them instead of running its own worldgen - giving the new dimension empty/garbage terrain. This hit the common ephemeral / pocket-dimension pattern, where ids recycle within a session. Also stops the persisted set growing without bound across create/destroy churn.
- **A throwing third-party event subscriber can no longer abort a shared operation.** Manifold is a shared library: several mods subscribe to the same registry (`Created`/`Destroyed`) and transit (`PlayerEntering`/`PlayerArriving`/`PlayerEntered`/`PlayerLeft`/`EntityChangedDimension`) events. Each subscriber is now invoked under its own guard, so one misbehaving handler's exception is logged and swallowed instead of propagating out and breaking another mod's dimension registration or player transit. Subscribers still run in registration order, and cancellation (`Cancel = true`) still works even if an earlier subscriber threw.
- **Separate-inventory swaps are now exception-safe.** `WithSeparateInventory` swaps capture the player's current contents before mutating any slot; the captured snapshot set is now always persisted, even if a category swap throws partway through (e.g. a malformed snapshot from an item a mod removed between visits). Previously a mid-swap failure skipped the persist, silently and permanently losing the player's items because the engine saves the physical inventory independently of Manifold's moddata.
- **A pending dimension could be claimed by a different owner mod.** `DefineForOwner` now rejects a pending dimension whose recorded owner mod id does not match the caller, so a colliding code cannot hijack another mod's dimension or corrupt owner attribution.
- **A failed `OnInitialize` is retried instead of generating uninitialized terrain.** The dimension is no longer marked initialized before init succeeds, so a transient init failure no longer leaves the strategy generating columns with unresolved block ids.
- **A corrupt manifest entry no longer aborts boot.** `SeedFromManifest` failures (out-of-range id, bad code) are logged and skipped, matching the manifest loader's drop-silently policy.
- **`TeleportBlock` no longer deletes the block** when the source and target resolve to the same position.
- **Stale `LastVisited` after engine-id reuse.** `PlayerPositionStore` entries for a destroyed dimension are evicted, so a later dimension reusing that id does not inherit old coordinates (also bounds the store's growth).
- **`Dispose` cleared the wrong side's service resolver.** In a singleplayer host the client and server share `ManifoldAccess`'s process-global resolvers; each `ModSystem` instance now clears only the resolver it installed.
- **Binary-compat shim** now used for all `Entity.Pos` reads (three player-lifecycle handlers were reading `entity.Pos` directly, which would crash on the other VS field/property shape).
- **Chunk-index math** uses floor division (correct for negative world coordinates) via a shared `ChunkMath` helper; the engine constants (chunk size, per-dimension chunk-Y stride) are centralized.
- **`DimensionAllocator`** validates a null code with `ArgumentNullException.ThrowIfNull` instead of a misleading empty-string guard.

### Changed
- **Dimension metadata is now immutable.** `IDimension.Metadata` is backed by an `ImmutableDictionary` (and the shared empty sentinel is `ImmutableDictionary.Empty`), so a consumer cannot downcast the read-only map back to `Dictionary` and mutate a registered dimension's snapshot out-of-band.

## [0.4.1] - 2026-06-13

### Fixed
- **Ephemeral dimensions never fired `Destroyed` on server shutdown.** The registry now removes every `Ephemeral` dimension during `ServerRunPhase.Shutdown`, firing the existing `Destroyed` event for each so companions (and any consumer subscribed to `IDimensionRegistry.Destroyed` or the client mirror's `IManifoldClient.Destroyed`) get a chance to clean up per-dimension state during a graceful stop. The manifest already skipped ephemeral entries when persisting, so on-disk savegame data is unchanged.

### Changed
- **ManifoldSample**: new `/createtempdim` and `/destroytempdim` commands exercising the ephemeral lifecycle (`Define().Ephemeral().Create()` + `Registry.TryRemove`), useful for verifying companions that subscribe to dimension destruction.

### Companion mods

- **Chart 0.2.0** released alongside. Highlights: a major performance fix (the map's dirty queue looped forever between adjacent chunks and re-rendered each column once per vertical chunk slice, pegging the client CPU inside custom dimensions - both loops are fixed and custom dims are now smooth), ephemeral-dim cache cleanup (reactive on `Destroyed` plus a defensive orphan scan at world load, closes #31), and an internal renderer refactor. Requires Manifold 0.4.1+. Release: https://github.com/Pixnop/Manifold/releases/tag/chart-v0.2.0

## [0.4.0] - 2026-05-29

### Added
- **`ITransitionService.TeleportBlock`** - cross-dimension block transit primitive. Moves a single block plus its `BlockEntity` state (inventory, attributes, BE-behaviors) from any source position to any target position in another dimension. The destination region is generated on demand (same `EnsureRegion` path as `TeleportEntity`); the `BlockEntity` is round-tripped through `BlockEntity.ToTreeAttributes` / `FromTreeAttributes` so inventories and attached state follow the block. Source slot is cleared after a successful move. Returns `true` when a non-air block was moved, `false` when the source slot was air. This completes the transit triplet (Player / Entity / Block). Engine glue lives in the new `BlockMover` (coverage-excluded, same pattern as `EntityMover`). Closes #36.
- **Per-dimension streaming budget** - `IDimensionBuilder.WithStreamingBudget(int maxColumnsPerTick)` (range 1..64) caps how many columns the streaming driver may ensure for the dimension per tick. Budgets are independent across dimensions: a busy dim cannot starve a quiet one. Default per-dim budget is 4 (matches the previous global cap). The streaming planner now partitions candidate columns by dimension and applies each dimension's budget separately; the driver resolves the per-dim budget from the registry on every tick. Closes #40.
- **Per-dimension metadata API** - `IDimensionBuilder.WithMetadata(string key, object? value)` attaches typed registration-time hints to a dimension; `IDimension.Metadata` exposes them as an `IReadOnlyDictionary<string, object?>`. A typed `GetMetadata<T>(key, defaultValue)` extension and a `HasMetadata(key)` predicate ship alongside. Supported value types: primitives, `string`, `enum`, `byte[]`, and `null`; other types throw `ArgumentException`. Duplicate keys on the same builder throw. Metadata is server-side only in v1 - not replicated to client mirrors, not persisted across server restarts (re-declare in your boot path). The built-in overworld and dimensions seeded from the manifest expose empty metadata. Closes #38.
- **`ITransitionService.PlayerArriving`** event - cancellable post-generation, pre-teleport hook on `TeleportPlayer`. Fires after the destination region has been generated and the final landing position resolved, but before the player is teleported. Subscribers can perform setup work that requires the target chunks to be loaded (place a welcome block, attach server-side state, log arrival metadata) or veto the transit by setting `Cancel = true`. The event fits between the existing `PlayerEntering` (pre-generation) and `PlayerLeft` / `PlayerEntered` (post-teleport). Closes #37.
- **`ITransitionService.EntityChangedDimension`** event - raised on the server after `TeleportEntity` re-homes a non-player entity successfully. The post-event mirrors the engine's `IEventAPI.PlayerDimensionChanged` (which covers `EntityPlayer`) and never fires for player entities. `EntityChangedDimensionEventArgs` exposes the entity, the previous and new `IDimension`, and the final landing `BlockPos`. The event is not raised when `TeleportEntity` throws. Closes #41.

### Companion mods

- **Chart 0.1.0** (alpha) released as a separate companion mod for dimension-aware world maps. Lives at [`companions/Chart/`](companions/Chart/) and has its own version line / tag scheme (`chart-vX.Y.Z`). Release: https://github.com/Pixnop/Manifold/releases/tag/chart-v0.1.0

## [0.3.1] - 2026-05-23

### Fixed
- **MissingFieldException at world load on Vintage Story 1.22.x.** VS 1.22 refactored `Entity.Pos` from a public field into a public property, and the IL emitted by 0.3.0 (built against the 1.21 API) throws `MissingFieldException` at runtime on 1.22 builds, which broke world generation for any player on 1.22.x. Manifold now resolves `Entity.Pos` through a small reflection-cached helper, so the same binary keeps working on every 1.21.x and 1.22.x release. The sample's `/sendtestitem` command uses `Entity.SidedPos` (a property in both shapes) for the same reason. Fixes #24.

### Changed
- The Manifold Sample now declares `manifold >= 0.3.0` in its modinfo (was 0.2.0), since it relies on the 0.3.0 `TeleportEntity` and `WithSeparateInventory` APIs.

## [0.3.0] - 2026-05-22

### Added
- **Per-dimension inventory** - opt-in `IDimensionBuilder.WithSeparateInventory(ManifoldInventory categories)` (flags: `Hotbar`, `Backpack`, `Character`, `All`). A dimension keeps its own player inventory for the chosen categories: entering swaps to the dimension's set (empty on the first visit) and leaving restores the previous one. Profiles are stored in player moddata (saved together with the physical inventory) so they survive logout and server restarts with no item loss; the snapshot is always taken before any slot is cleared.
- **Non-player entity transit** - `ITransitionService.TeleportEntity(Entity entity, AssetLocation targetDim, TransitionOptions options = default)` moves items and other non-player entities between dimensions. The destination region is generated on demand, then the entity is re-homed into it. Use `TeleportPlayer` for players (`TeleportEntity` rejects player entities).

### Changed
- **Breaking:** `ITargetPositionResolver.Resolve` now takes the source `Entity` instead of `IServerPlayer`, so one resolver serves both player and entity transit. A custom resolver must change its parameter from `IServerPlayer player` to `Entity entity` and read the source X/Z from `entity.Pos`. The built-in resolvers and `TeleportPlayer` are unaffected.

## [0.2.0] - 2026-05-22

### Added
- **Streaming worldgen** - opt-in per-dimension streaming via `IDimensionBuilder.Streaming(loadRadius)` (range 1..32). Generates chunks on demand as players move, keeping a window of `loadRadius` chunks around each player via a per-tick budget; unloading of distant chunks is delegated to the engine. On transit, a synchronous landing pad (the dimension's `WithGenerationRadius` region) is still generated first so the player never spawns in void; the streaming driver then fills the surrounding window over subsequent server ticks. Bounded generation (`WithGenerationRadius`) remains the default; the two modes coexist.
- **Streaming radius covers the view** - the effective streaming radius is `max(loadRadius, server view distance)`, so generated chunks always reach as far as the player can see.
- **`WithRelightHeight(maxY)`** - builder option to set the relight band height per dimension (default 20), so taller dimensions can be lit correctly.
- **ManifoldSample: `/streamdim` command** - demo command that transits to the new `manifoldsample:stream` streaming dimension, demonstrating chunk streaming in action.

### Performance
- Streamed columns are relit individually over their own footprint instead of the spanning bounding box of each tick's batch, making relight cost proportional to the number of new columns and eliminating server-overload spikes during streaming.

## [0.1.0] - 2026-05-20

### Added
- **Dimension registration** - fluent `IDimensionBuilder` API to declare `Persistent` or `Ephemeral` dimensions at boot (`RegisterStatic`) or at runtime (`Create`), identified by `AssetLocation` codes.
- **Active bounded-region worldgen** - `IWorldgenStrategy` (two-method contract: `OnInitialize` + `GenerateColumn`); Manifold drives pre-generation of a configurable-radius chunk square around the transit target before the player arrives. Configurable via `WithGenerationRadius` (default 2, range 0-16).
- **Player transit** - `ITransitionService.TeleportPlayer` with cancellable `PlayerEntering`, and `PlayerLeft`/`PlayerEntered` events.
- **Travel policy per dimension** - `SpawnBehavior` enum (`SameCoordinates`, `DimensionSpawn`, `LastVisited`); `WithFixedSpawn`; `WithForcedGameMode`; per-transit override via `TransitionOptions`.
- **Persistence** - dimension manifest (codes, internal ids, owner mod ids, states), generated-column set, and per-player last-visited positions stored in the VS savegame. Idempotent across server restarts.
- **Quarantine** - dimensions whose owning mod is absent on load are quarantined (chunks preserved, transit refused); released via `/manifold purge <code>`.
- **Client mirror** - `IManifoldClient` exposes a replicated read-only dimension list on the client side via `capi.GetManifoldClient()`.
- **Owner attribution** - `sapi.GetManifoldServer(thisModSystem)` returns an owner-scoped facade; `Registry.Define` called through it records the caller's mod id, enabling correct quarantine on uninstall.
- **Built-in overworld** - `manifold:overworld` (id 0) is a first-class Manifold dimension, transitable via the standard API.
- **Zero Harmony patches** - all functionality built on the public `VintagestoryAPI` surface.
- **Opt-in helpers**: `BasicVoidWorldgenStrategy` (no-op air dimension), `PortalBlockBase` (collision-triggered transit block base class), `DimensionCommandBuilder` (fluent chat-command builder for transit).
- **ManifoldSample** consumer mod demonstrating void + flat dimensions and `/voiddim`, `/flatdim`, `/overworlddim` commands.
