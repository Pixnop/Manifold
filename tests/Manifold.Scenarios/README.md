# Manifold.Scenarios

Integration scenarios running Manifold inside a real headless Vintage Story
server, through [Atlas](https://github.com/Pixnop/Atlas) (`Pixnop.Atlas.XUnit`).
They complement `Manifold.Pure.Tests`: the pure tests pin down unit-level
contracts with fakes, these scenarios pin down actual engine behavior.

## Running locally

Requirements: .NET 10 SDK, a Vintage Story 1.22.x install (Atlas 0.11.0), and
the `VINTAGE_STORY` environment variable pointing at the folder containing
`VintagestoryAPI.dll`.

    dotnet test tests/Manifold.Scenarios

Each scenario class boots its own embedded server (about 7 s). Scenario
classes never run in parallel (one live server per process).

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

The admin and lifecycle surface is covered end to end: `/manifold purge`
(refusals, evacuation, id release), `ForceRemoveDimension`, the ephemeral
auto-reap, `WithDarkSky` column capping, typed `WithMetadata` round trips,
and the void rescue after a kicked player's dimension disappears
(`ITestPlayer.IsConnected`). `PersistenceScenarios` uses Atlas's
`RestartWorld` isolation to assert dimension ids and generated terrain
survive a real server shutdown/boot round trip.
