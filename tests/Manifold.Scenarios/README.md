# Manifold.Scenarios

Integration scenarios running Manifold inside a real headless Vintage Story
server, through [Atlas](https://github.com/Pixnop/Atlas) (`Pixnop.Atlas.XUnit`).
They complement `Manifold.Pure.Tests`: the pure tests pin down unit-level
contracts with fakes, these scenarios pin down actual engine behavior.

## Running locally

Requirements: .NET 10 SDK, a Vintage Story 1.22.x install (Atlas 0.7.0), and
the `VINTAGE_STORY` environment variable pointing at the folder containing
`VintagestoryAPI.dll`.

    dotnet test tests/Manifold.Scenarios

Each scenario class boots its own embedded server (about 7 s). Scenario
classes never run in parallel (one live server per process).

## Isolation modes

Most scenarios share their class host's world and isolate through disjoint
coordinates, unique entity ids, and unique dimension paths. Scenarios that
mutate dimension 0 and involve no players use `RollbackWorld = true`: the
host's world is restored from a snapshot instead of paying a full recycle.
Classes that treat that rollback as a contract rather than an optimization
(SmokeScenarios, OverworldTransitScenarios) add `StrictIsolation = true`
(Atlas 0.7.0), so a silent degrade to a full recycle fails the scenario
instead of just slowing the suite down. Stage 1 rollback covers dimension 0
only, so the fixture never generates mini-dimension terrain at boot;
scenarios that probe a dimension's terrain request it with
`/atlasfx pregen <dimpath>`. Classes that cannot roll back carry a
`rollback-stage2-candidate` (joined players) or `rollback-stage3-candidate`
(mini-dimension world state) comment stating what a future rollback stage
would need.

Persistence scenarios use `RestartWorld = true` (Atlas 0.7.0): the class host
is shut down gracefully and a replacement boots against the persisted save,
so DimensionPersistenceScenarios asserts on what actually survives a real
save/load round trip (the dimension manifest: static dimensions re-claimed
under the same internal id, runtime persistent ones back as Pending, ephemeral
ones dropped). The pre-restart state is seeded by the fixture at first boot,
requested through an `[AtlasDataFiles]`-staged ModConfig, because Atlas does
not guarantee scenario order within a class; each restart scenario is
self-sufficient in any order.

## Architecture

Scenario code cannot call the Manifold API directly: the ModLoader loads its
own copy of Manifold.dll, so its statics and types are not the ones the test
assembly references. Everything that talks to Manifold lives in the staged
companion mod `Manifold.Scenarios.FixtureMod` (modid `atlasfixture`), which
registers deterministic test dimensions and exposes `/atlasfx` server
commands. Command outcomes are asserted directly on the `CommandResult`
returned by `ExecuteCommand`; boot-published state (dimension ids) and
transit event observations still flow through `SaveGame` data.

Player-dependent paths (player transit, per-dimension inventory swap,
concurrent players across dimensions) run against headless test players
joined through Atlas's `World.JoinPlayer`.
