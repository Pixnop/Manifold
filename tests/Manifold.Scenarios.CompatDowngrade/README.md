# Manifold.Scenarios.CompatDowngrade

Proves, on a real embedded Vintage Story server through Atlas, that a world saved by this
repo's dev build (which also writes the `manifold:schema` sidecar) survives opening with the
published Manifold 0.5.1 release: the **downgrade** direction (`DowngradeVerifyScenarios`). The
**upgrade** direction lives in the sibling project `Manifold.Scenarios.Compat`; see its README's
"Why two projects instead of one" for why the two directions are not one project with two
classes (short version: a process can only bind one physical build of Manifold's frozen-identity
dll, so each direction needs its own process, and `dotnet test` gives one process per project).

Loads the savegame fixture under `fixtures/`, generated before each run by
`tests/Manifold.Scenarios.CompatFixtures` (see that project's README for how and why) and
asserts that everything a world must keep survives: the same persistent dimension and internal id, its
generated terrain intact including a block a player placed, the player's last-visited position,
their separate-inventory profile, and their saved pre-forced game mode. This direction
additionally asserts the schema sidecar itself, something 0.5.1 has never heard of, survives
unharmed rather than being stripped or crashing the boot.

## Running locally

    VINTAGE_STORY=/path/to/vintagestory dotnet test tests/Manifold.Scenarios.CompatDowngrade -c Release

Requires the same VINTAGE_STORY setup as `Manifold.Scenarios` (Atlas 0.15.0). Boots its own
embedded server against the published 0.5.1 zip, downloaded once and cached under `obj/` (same
as `Manifold.Scenarios.Compat`; see the csproj).

## Architecture

Scenario code here never touches Manifold types directly, the same rule `Manifold.Scenarios`
follows and for the same reason: the ModLoader loads its own copy of whichever Manifold.dll is
staged, a different assembly identity than anything this project could reference at compile
time even if it wanted to (this project in particular carries no `ProjectReference` to
`src/Manifold` at all, see the csproj comment on why one would be actively harmful here).
Everything that talks to Manifold's API lives in `Manifold.Scenarios.CompatFixtureMod` (see its
own doc comment), reached through two `/manicompat` commands (`enter`, `leave`) and read back
through `SaveGame` data, the same boundary `ManifoldScenarioBase` uses in `Manifold.Scenarios`.

A joined Atlas test player gets a UID derived from its name (`atlas-<name>`), so joining
`compatdev` here, the SAME name the matching builder scenario used, resumes that exact player:
their position, game mode and inventory as the dev build's boot last wrote them. This is the
mechanism behind every per-player assertion in this scenario.
