# Manifold.Scenarios.Compat

Proves, on a real embedded Vintage Story server through Atlas, that worlds survive moving
between the published Manifold 0.5.1 release and this repo's dev build, in both directions:

- **Upgrade** (`UpgradeVerifyScenarios`): a world played entirely with the published 0.5.1
  mod opens with the dev build.
- **Downgrade** (`DowngradeVerifyScenarios`): a world saved by the dev build (which also
  writes the `manifold:schema` sidecar) opens with the published 0.5.1 release.

Each loads one of the two committed savegame fixtures under `fixtures/` (built by
`tests/Manifold.Scenarios.CompatFixtures`; see that project's README for how and why) and
asserts everything the brief named survives: the same persistent dimension and internal id,
its generated terrain intact including a block a player placed, the player's last-visited
position, their separate-inventory profile, and their saved pre-forced game mode. The
downgrade direction additionally asserts the schema sidecar itself, something 0.5.1 has never
heard of, survives unharmed rather than being stripped or crashing the boot.

## Running locally

    VINTAGE_STORY=/path/to/vintagestory dotnet test tests/Manifold.Scenarios.Compat -c Release

Requires the same VINTAGE_STORY setup as `Manifold.Scenarios` (Atlas 0.15.0). Each class
boots its own embedded server: one against the dev build (the assembly-default mod set,
staged the same way `Manifold.Scenarios` stages it), one against the published 0.5.1 zip
(downloaded once and cached under `obj/`, same as `Chart.Scenarios`; see the csproj).

### A one-process-per-build limit, not a scenario limit

Run the two scenarios with `dotnet test` (used above and by CI) or `atlas run --parallel`
(each class on its own worker subprocess) and both pass reliably. A plain `atlas run` with no
`--parallel` runs every class of a dll in ONE process, and that does NOT reliably work here:
whichever class boots its Manifold build SECOND in that process fails with `Could not load
file or assembly 'Manifold, Version=0.1.0.0, ...'. Assembly with same name is already
loaded`. Manifold's `AssemblyVersion` is deliberately frozen across releases (see
`Directory.Build.props` and `Manifold.Scenarios.CompatFixtureMod`'s csproj), which is exactly
what lets a companion mod and, empirically, an embedded server's mod loader keep loading
EITHER build on its own; it also means two DIFFERENT physical `Manifold.dll` files share that
one identity, and the process only ever binds the first one it loads under that name. This
is a real, reproducible limit of running two different builds of the same-identity assembly
in one .NET process, observed here across ten repeated runs (five orderings each of
`atlas run` with no filter, always failing whichever class ran second, and five of `dotnet
test`/`atlas run --parallel`, ten-for-ten green), not a flaky scenario: do not "fix" a red
`atlas run` (no `--parallel`) result here by rewriting the scenarios, re-run it with
`--parallel` or use `dotnet test` instead.

## What Atlas could and could not do here

Everything the brief asked for is covered by real, restartable, engine-backed assertions;
nothing was impossible. The one accommodation: `atlas fixture` (Atlas 0.15.0), not
`[AtlasScenario(RestartWorld = true)]`, is what actually produces the two fixture saves - see
`Manifold.Scenarios.CompatFixtures/README.md`'s "Why a separate project" section for why
`RestartWorld` alone cannot (it restarts the class host BEFORE the scenario body that would
need to seed the world runs, and scenario order within a class is not guaranteed, so two
methods in one class cannot reliably do "seed, then restart, then harvest" either). This
is not a gap: `atlas fixture` is Atlas's own documented answer to exactly this need.

## Architecture

Scenario code here never touches Manifold types directly, the same rule
`Manifold.Scenarios` follows and for the same reason: the ModLoader loads its own copy of
whichever Manifold.dll is staged, a different assembly identity than anything this project
could reference at compile time even if it wanted to. Everything that talks to Manifold's API
lives in `Manifold.Scenarios.CompatFixtureMod` (see its own doc comment), reached through two
`/manicompat` commands (`enter`, `leave`) and read back through `SaveGame` data, the same
boundary `ManifoldScenarioBase` uses in `Manifold.Scenarios`.

A joined Atlas test player gets a UID derived from its name (`atlas-<name>`), so joining
`compat051` (or `compatdev`) here, the SAME name the matching builder scenario used, resumes
that exact player: their position, game mode and inventory as the OTHER build's boot last
wrote them. This is the mechanism behind every per-player assertion in these scenarios.
