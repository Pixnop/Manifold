---
_layout: landing
---

# Manifold

**A Vintage Story 1.22 library mod for declaring and managing custom dimensions.**

Manifold gives consumer mods a clean, Harmony-free public API to create persistent or ephemeral dimensions, supply procedural worldgen, and transit players between them - all without touching engine internals.

## Highlights

- **Declare dimensions** statically at boot or dynamically at runtime, as Persistent or Ephemeral.
- **Active worldgen**: Manifold pre-generates a bounded region around the transit target before the player arrives (away from the world's negative edge, they never land in a void), plus an opt-in streaming mode that keeps generating chunks as players move.
- **Transit triplet**: `ITransitionService.TeleportPlayer`, `TeleportEntity`, and `TeleportBlock` move players, non-player entities, and single blocks (with their `BlockEntity` state) between dimensions, with a full set of cancellable transit events.
- **Travel policy per dimension**: spawn behavior (same coordinates, fixed spawn, or last-visited position) plus an optional forced game mode that is restored on the way out, all configured through a fluent builder.
- **Per-dimension inventory**: `WithSeparateInventory` gives a dimension its own player inventory for chosen categories, swapped on entry and exit.
- **Dark dimensions and relight**: `WithDarkSky` seals a dimension so it stays dark without a per-dimension day/night cycle, and `RelightRegion` / `/manifold relight` recalculate light after runtime block placement.
- **Safe teardown**: a dimension is never removed while a player is inside it; `ForceRemoveDimension` and `/manifold purge` evacuate occupants first, ephemeral dimensions reap themselves when emptied, and a player whose dimension is gone is rescued to the overworld on join.
- **Savegame persistence**: dimension manifest, generated-column set, and per-player positions survive server restarts. Dimensions from uninstalled mods are quarantined (chunks kept, transit refused).
- **Client mirror**: the dimension list, with metadata, is replicated to connected clients, which also learn when the local player transits.
- **Zero Harmony patches**: built entirely on the public VintagestoryAPI.
- **Opt-in helpers**: `PortalBlockBase`, `DimensionCommandBuilder`, `BasicVoidWorldgenStrategy`.

## Getting Started

Add Manifold as a dependency in your `modinfo.json`, get the facade, and register a dimension - in under 20 lines. See the [Getting Started](articles/getting-started.md) guide.

## Browse the Docs

| Section | Description |
|---------|-------------|
| [Getting Started](articles/getting-started.md) | Dependency wiring and a minimal consumer mod |
| [Dimensions](articles/dimensions.md) | Lifecycle, registry, codes, quarantine |
| [Worldgen](articles/worldgen.md) | IWorldgenStrategy, active generation model |
| [Transit & Travel Policy](articles/transit-and-travel-policy.md) | Teleport API, spawn behaviors, helpers |
| [Architecture](articles/architecture.md) | Internal services, sided split, zero-Harmony note |
| [API Reference](api/Manifold.Api.yml) | Auto-generated from XML documentation |

## Compatibility

- Vintage Story **1.22.x** (the integration suite runs on 1.22.7)
- **.NET 10**
- No Harmony reference; protobuf is provided by the game itself

## License

[MIT](https://github.com/Pixnop/Manifold/blob/main/LICENSE)
