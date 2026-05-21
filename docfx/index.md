---
_layout: landing
---

# Manifold

**A Vintage Story 1.21+ library mod for declaring and managing custom dimensions.**

Manifold gives consumer mods a clean, Harmony-free public API to create persistent or ephemeral dimensions, supply procedural worldgen, and transit players between them - all without touching engine internals.

## Highlights

- **Declare dimensions** statically at boot or dynamically at runtime, as Persistent or Ephemeral.
- **Active worldgen**: Manifold pre-generates a bounded region around the transit target before the player arrives, so they never land in a void.
- **Transit API**: one call - `ITransitionService.TeleportPlayer` - moves a player with configurable spawn behavior (same coordinates, fixed spawn, or last-visited position).
- **Travel policy per dimension**: spawn behavior + optional forced game mode, all configured through a fluent builder.
- **Savegame persistence**: dimension manifest, generated-column set, and per-player positions survive server restarts. Dimensions from uninstalled mods are quarantined (chunks kept, transit refused).
- **Client mirror**: the dimension list is replicated to connected clients.
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

- Vintage Story **1.21+**
- **.NET 10**
- No Harmony required (0Harmony and protobuf are provided by the game itself)

## License

[MIT](https://github.com/Pixnop/Manifold/blob/main/LICENSE)
