# Manifold.Scenarios

Integration scenarios running Manifold inside a real headless Vintage Story
server, through [Atlas](https://github.com/Pixnop/Atlas) (`Pixnop.Atlas.XUnit`).
They complement `Manifold.Pure.Tests`: the pure tests pin down unit-level
contracts with fakes, these scenarios pin down actual engine behavior.

## Running locally

Requirements: .NET 10 SDK, a Vintage Story 1.22.x install (Atlas 0.3.0), and
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
registers deterministic test dimensions, exposes `/atlasfx` server commands,
and publishes results through `SaveGame` data read back by the scenarios.

Player-dependent paths (player transit, per-dimension inventory swap) run
against a headless test player joined through Atlas's `World.JoinPlayer`.
Known limit: one test player per world, so concurrent multi-player behavior
is not covered yet (Pixnop/Atlas#26).
