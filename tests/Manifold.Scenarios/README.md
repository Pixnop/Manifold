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
mutate dimension 0 and involve no players use `RollbackWorld = true` (Atlas
0.6.0): the host's world is restored from a snapshot instead of paying a full
recycle. Stage 1 rollback covers dimension 0 only, so the fixture never
generates mini-dimension terrain at boot; scenarios that probe a dimension's
terrain request it with `/atlasfx pregen <dimpath>`. Classes that cannot roll
back carry a `rollback-stage2-candidate` (joined players) or
`rollback-stage3-candidate` (mini-dimension world state) comment stating what
a future rollback stage would need.

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
