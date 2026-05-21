# Changelog

All notable changes to Manifold will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
