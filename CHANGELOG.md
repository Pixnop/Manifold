# Changelog

All notable changes to Manifold will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
