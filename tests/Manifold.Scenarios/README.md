# Manifold.Scenarios

Integration scenarios running Manifold inside a real headless Vintage Story
server, through [Atlas](https://github.com/Pixnop/Atlas) (`Pixnop.Atlas.XUnit`).
They complement `Manifold.Pure.Tests`: the pure tests pin down unit-level
contracts with fakes, these scenarios pin down actual engine behavior.

## Running locally

Requirements: .NET 10 SDK, a Vintage Story 1.22.x install (Atlas 0.15.1; CI runs the scenarios on 1.22.7), and
the `VINTAGE_STORY` environment variable pointing at the folder containing
`VintagestoryAPI.dll`.

    dotnet test tests/Manifold.Scenarios

Each scenario class boots its own embedded server (about 7 s). Scenario
classes never run in parallel (one live server per process).

## Isolation modes

Most scenarios share their class host's world and isolate through disjoint
coordinates, unique entity ids, and unique dimension paths. Some scenarios
with no joined players use `RollbackWorld = true` (currently `SmokeScenarios`,
`EntityTransitScenarios`, `BlockTransitScenarios`, `OverworldTransitScenarios`,
`EphemeralDimensionScenarios`, `DimensionLifecycleScenarios`, and
`RecyclingScenarios`); others that also join no player (`AdminCommandScenarios`,
`DarkSkyScenarios`, `DimensionMetadataScenarios`, `DimensionWorldgenScenarios`)
isolate through disjoint coordinates instead. The host's world is restored
from a snapshot instead of paying a full recycle. Since Atlas 0.8.0 the
snapshot covers mini-dimension chunk columns too, and Manifold cooperates
through the `atlas:rollback:restored` event bus hook: after every restore,
`ManifoldModSystem.OnAtlasRollbackRestored` re-runs the boot hydrate,
rebuilding the registry, the id allocator, and the persisted-store mirrors
from the restored SaveGame (the manifest, `manifold:genchunks`,
`manifold:lastpos`). Classes that treat the rollback as a contract rather
than an optimization add `StrictIsolation = true`, so a silent degrade to a
full recycle fails the scenario instead of just slowing the suite down; the
former stage 3 candidates (EntityTransit, BlockTransit, Ephemeral,
Lifecycle) are all strict now, and EphemeralDimensionScenarios carries the
desync-gone proof: re-creating a dimension whose first incarnation only a
rollback removed. Boot-time mini-dimension terrain no longer disqualifies
rollback; the fixture still generates terrain on demand only
(`/atlasfx pregen <dimpath>` or a transit) purely to keep snapshots small.
Classes with joined players cannot roll back yet. Six of them
(`MultiPlayerScenarios`, `PlayerInventoryScenarios`, `TransitEventScenarios`,
`PlayerTransitScenarios`, `StreamingWorldgenScenarios`, `RealTerrainLandingScenarios`)
carry a `rollback-stage2-candidate` comment stating what a future rollback stage
would need; the others that join players (`ClientMirrorScenarios`,
`CommandBuilderScenarios`, `InventoryAccessScenarios`, `QuarantineScenarios`,
`ReconnectScenarios`, `TeardownScenarios`, `TravelPolicyScenarios`) do not
carry that comment yet.

Persistence scenarios use `RestartWorld = true` (Atlas 0.7.0): the class host
is shut down gracefully and a replacement boots against the persisted save,
so DimensionPersistenceScenarios asserts on what actually survives a real
save/load round trip (the dimension manifest: static dimensions re-claimed
under the same internal id, runtime persistent ones back as Pending, ephemeral
ones dropped). The pre-restart state is seeded by the fixture at first boot,
requested through an `[AtlasDataFiles]`-staged ModConfig, because Atlas does
not guarantee scenario order within a class; each restart scenario is
self-sufficient in any order.

## What is covered

102 scenarios, one class per area:

| Area | Classes |
| --- | --- |
| Boot, registration, per-dimension worldgen, streaming, dark sky | `SmokeScenarios`, `DimensionWorldgenScenarios`, `StreamingWorldgenScenarios`, `DarkSkyScenarios` |
| Lifecycle, teardown, admin commands | `DimensionLifecycleScenarios`, `EphemeralDimensionScenarios`, `TeardownScenarios`, `AdminCommandScenarios` |
| Transit of entities, blocks and players, round trips | `EntityTransitScenarios`, `BlockTransitScenarios`, `PlayerTransitScenarios`, `OverworldTransitScenarios`, `InventoryAccessScenarios` |
| Transit events: order, vetoes, a throwing subscriber | `TransitEventScenarios` |
| Travel policy, forced game mode, command builders | `TravelPolicyScenarios`, `CommandBuilderScenarios` |
| Per-dimension inventory, concurrent players, reconnection | `PlayerInventoryScenarios`, `MultiPlayerScenarios`, `ReconnectScenarios` |
| Metadata, id recycling | `DimensionMetadataScenarios`, `RecyclingScenarios` |
| What the client receives (Manifold's packets) | `ClientMirrorScenarios` |
| Persistence and quarantine across a real restart | `DimensionPersistenceScenarios`, `QuarantineScenarios` |
| Landing position on real terrain (`TargetPositionResolvers.SameXZSurfaceY`) | `RealTerrainLandingScenarios` |
| The sample consumer mod (`samples/ManifoldSample`), staged as a real Atlas mod | `ManifoldSampleMiningScenarios`, `ManifoldSampleSmokeScenarios` |
| Cross-version save compatibility (0.5.1 <-> dev) | `UpgradeVerifyScenarios` (project `Manifold.Scenarios.Compat`), `DowngradeVerifyScenarios` (project `Manifold.Scenarios.CompatDowngrade`) |

`ClientMirrorScenarios` decodes Manifold's own network packets through
Atlas's client observations (`player.Client.Packets<T>`), deserialized into
the internal packet types (Manifold grants this project internals access).
`QuarantineScenarios` stages a ModConfig that makes the fixture append, on
every world save, a manifest entry owned by a mod that is not installed; the
restarted server must bring it back Quarantined.

`ManifoldSampleMiningScenarios` and `ManifoldSampleSmokeScenarios` stage
`samples/ManifoldSample` itself (modid `manifoldsample`) as a third Atlas
mod and drive its real `/voiddim`, `/flatdim`, `/darkdim`, `/streamdim`,
`/vaultdim`, `/overworlddim`, `/sendtestitem`, `/sendtestblock`,
`/createtempdim`, `/destroytempdim`, `/miningdim` and `/miningreset`
commands as joined players, the sample's own manual smoke checklist
(samples/ManifoldSample/README.md) run through Atlas instead of a hand
session. The mining dimension gets its own class because `/miningreset`
needs the fuller create/reuse/reset/reset-again/recreate narrative (a
single global dimension code, so the whole lifecycle lives in one
scenario rather than several whose order Atlas does not guarantee); every
other command gets one smoke scenario each in the sibling class. Two
items on the sample's checklist looked at first like they needed a real
client to drive (aim/raycast for `/sendtestblock`, walking into the void
portal block), but both are reachable through `ITestPlayer.Entity`, the
documented escape hatch onto the live `EntityPlayer`:
`IServerPlayer.CurrentBlockSelection` is just `Entity.BlockSelection`, a
public field, so a scenario sets the caller's aim directly instead of
raycasting for it; and the portal's `OnEntityCollide` fires from the
engine's own server-side collision resolution
(`IRemotePhysics.OnReceivedClientPos`, the same handler a real client's
position packet drives, reachable via `Entity.SidedProperties.Behaviors`),
so replaying it with a moved entity position is a real collision. The
boot scenario also asserts the portal block itself resolves
(`World.GetBlock(manifoldsample:voidportal)`), proving the portal's own
asset staging (the `StageAtlasFolderMods` target had to learn to copy
`samples/ManifoldSample/assets/` too, since neither the fixture nor
Manifold itself ships assets).

Cross-version compatibility (a world moving between the published 0.5.1 release and this
dev build, in both directions) is a separate concern from this project's restart coverage
above, which only ever restarts within ONE Manifold build: see `Manifold.Scenarios.Compat`
and `Manifold.Scenarios.CompatDowngrade` (verifier scenarios, one project per direction) and
`Manifold.Scenarios.CompatFixtures` and `Manifold.Scenarios.CompatFixturesUpgrade` (the
savegame fixtures they load, also split one project per direction, and for the same reason).

Two Manifold bugs were found this way and fixed in the same release: a forced
game mode leaking out of its dimension, and the transit packet carrying the
position the player left instead of the landing position.

## Architecture

Scenario code cannot call the Manifold API directly: the ModLoader loads its
own copy of Manifold.dll, so its statics and types are not the ones the test
assembly references. Everything that talks to Manifold lives in the staged
companion mod `Manifold.Scenarios.FixtureMod` (modid `atlasfixture`), which
registers deterministic test dimensions and exposes `/atlasfx` server
commands. Command outcomes are asserted directly on the `CommandResult`
returned by `ExecuteCommand`; boot-published state (dimension ids) and
transit event observations still flow through `SaveGame` data. The sample
mod follows the same rule: it is exercised only through its own chat
commands and the fixture's generic `/atlasfx state <domain:path>` (which
accepts any fully qualified dimension code, not just the fixture's own),
never through a direct reference.

Player-dependent paths (player transit, per-dimension inventory swap,
concurrent players across dimensions) run against headless test players
joined through Atlas's `World.JoinPlayer`.
