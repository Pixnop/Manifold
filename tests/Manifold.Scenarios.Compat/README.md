# Manifold.Scenarios.Compat

Proves, on a real embedded Vintage Story server through Atlas, that a world played entirely
with the published Manifold 0.5.1 release survives opening with this repo's dev build: the
**upgrade** direction (`UpgradeVerifyScenarios`). The **downgrade** direction (a world saved by
the dev build opened with the published 0.5.1 release) lives in the sibling project
`Manifold.Scenarios.CompatDowngrade`; see "Why two projects instead of one" below for why they
are not one project with two classes.

Loads the committed savegame fixture under `fixtures/` (built by
`tests/Manifold.Scenarios.CompatFixtures`; see that project's README for how and why) and
asserts that everything a world must keep survives: the same persistent dimension and internal id, its
generated terrain intact including a block a player placed, the player's last-visited position,
their separate-inventory profile, and their saved pre-forced game mode.

## Running locally

    VINTAGE_STORY=/path/to/vintagestory dotnet test tests/Manifold.Scenarios.Compat -c Release

Requires the same VINTAGE_STORY setup as `Manifold.Scenarios` (Atlas 0.15.0).

## Why two projects instead of one

Verifying both directions needs, in the same test run, two DIFFERENT physical builds of a
same-identity assembly: Manifold's `AssemblyVersion` is deliberately frozen across releases (see
`Directory.Build.props`), which is exactly what lets a companion mod and an embedded server's
mod loader keep loading EITHER build on its own, but it also means the .NET process can only
ever bind ONE of the two dlls under that identity, no matter how Atlas's own mod loader is told
to stage them. A single test process (what one `dotnet test` invocation of one project is) that
tries to boot the dev build for one class and the 0.5.1 release for another therefore cannot
genuinely run both: whichever build's dll a `ProjectReference` happens to copy into the shared
output directory wins default assembly probing for the WHOLE process, silently, for every class
in it, regardless of what each class's own `[AtlasWorld(Mods = [...])]` says to stage. This was
caught, not theorized: with both directions in one project, `DowngradeVerifyScenarios` kept
passing while actually booting the dev build's `Manifold.dll` the entire time (it logs
`"[Manifold] Initialized."`, the dev build's own message; the 0.5.1 release logs `"[Manifold]
Initialized (healthy)."` and `"Harmony ready"`, neither of which ever appeared): the assertions
happened to hold for the wrong build, because a same-schema save loads back fine regardless of
which build reads it, so nothing failed loudly.

The fix is not a build-system flag: it is giving each direction its OWN process, since a process
can only ever bind one physical dll under Manifold's frozen identity. Two separate test projects
give exactly that (`dotnet test` runs one project's tests in one process): this project has an
ordinary `ProjectReference` to `src/Manifold` (so `UpgradeVerifyScenarios` gets the dev build,
the direction it actually needs) and no reference to the 0.5.1 zip at all; `Manifold.Scenarios.
CompatDowngrade` has no `ProjectReference` to `src/Manifold` at all and stages the 0.5.1 release
zip explicitly instead, so there is no dev-build dll anywhere in ITS output to collide with.
Running both classes as two `dotnet test` invocations (as CI does) gives each its own process
and, for the first time, each direction genuinely runs against the build it claims to.

An earlier version of this doc described a "one-process-per-build CLR limit" that only a plain
`atlas run` (no `--parallel`) supposedly hit, with `dotnet test` claimed safe. That was backwards:
`dotnet test` was never actually loading two different builds in one process in the first place
(see above), so it could not have been exercising, let alone surviving, the one-process limit;
the limit is real, but it is not something `dotnet test` on the old one-project layout ever
avoided.

## What Atlas could and could not do here

Everything listed above is covered by real, restartable, engine-backed assertions;
nothing was impossible. The one accommodation: `atlas fixture` (Atlas 0.15.0), not
`[AtlasScenario(RestartWorld = true)]`, is what actually produces the fixture save; see
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
`compat051` here, the SAME name the matching builder scenario used, resumes that exact player:
their position, game mode and inventory as the 0.5.1 build's boot last wrote them. This is the
mechanism behind every per-player assertion in this scenario.
