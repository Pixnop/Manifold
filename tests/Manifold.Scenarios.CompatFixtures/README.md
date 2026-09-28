# Manifold.Scenarios.CompatFixtures

Builds the two savegame fixtures `tests/Manifold.Scenarios.Compat` loads:
`fixtures/upgrade-from-0.5.1.vcdbs` (played entirely with the published Manifold 0.5.1
release) and `fixtures/downgrade-from-dev.vcdbs` (played with this repo's dev build). Not a
test suite that runs in CI: a one-time, re-runnable generator. Both fixtures are committed
(about 2 MB each), so an ordinary contributor never needs to run this project at all.

## Why a separate project, and why savegame fixtures at all

`[AtlasWorld(SaveFile = "...")]` (Atlas 0.7.0+) loads a prebuilt world save instead of
generating one, which is exactly what proving a real cross-version save/load round trip
needs: a class staged with one Manifold build boots against a save a DIFFERENT build
actually produced, the same way a player's world folder does when they update the mod. That
save has to come from somewhere. `RestartWorld = true` cannot produce it here: it restarts
the CURRENT class host, before the scenario body that would need to run first (see the
Atlas.XUnit.AtlasScenarioAttribute.RestartWorld docs), so nothing can seed interactive state
(join a player, place a block, move, leave) and then trigger its own restart from inside one
scenario method. Two methods in one class do not fix it either: Atlas does not guarantee
scenario order within a class, and the seeding here needs Atlas-only capabilities (a joined
test player) that cannot instead run unconditionally at boot the way the existing
`SeedPersistenceFixtures` pattern in `Manifold.Scenarios.FixtureMod` does for its
ModConfig-driven, non-interactive seeding.

Atlas 0.15.0 ships exactly the missing piece: `atlas fixture <dll> --scenario <substring>
--out <fixture.vcdbs>` runs ONE ordinary `[AtlasScenario]` as a world builder, then copies
the world save its own graceful shutdown persisted to `--out` (`Atlas.XUnit.Internal.
HostRegistry.ShutDownAndHarvestSavePathAsync`, "the harvest seam of atlas fixture"). The
builder scenario just needs to do the setup and return; the CLI handles the save-then-copy.
This is the officially documented way to build an Atlas fixture, so it is what this project
uses; the version-crossing part (staging the 0.5.1 zip for one builder, the dev build for the
other) is the only piece specific to this task.

Committing the two output files, rather than generating them in CI, was the simpler of the
two options the brief allowed for ("committed if small, or generated in CI by one project and
consumed by the other"): at ~2 MB each they cost little in the repo, and skipping a
build-then-harvest step in CI (with its own ordering and mod-staging concerns, see below) for
every PR keeps the e2e job's runtime down and removes a whole class of "the generator changed
but nobody regenerated the fixture" staleness a CI-time build cannot introduce.
Regenerating them only needs re-running this project's two scenarios; see below.

## The fixture mod

Both builder scenarios drive the SAME compiled fixture,
`tests/Manifold.Scenarios.CompatFixtureMod` (modid `manicompat`), compiled only against the
published `Pixnop.Manifold` 0.5.1 NuGet package. It is staged against the 0.5.1 release zip
for `BuildTheUpgradeFixture` and against this repo's dev-built `Manifold.dll` for
`BuildTheDowngradeFixture`, unmodified either way: Manifold's `AssemblyVersion` is frozen
(`Directory.Build.props`, "companions compiled against Manifold must keep loading across
releases") specifically so a single companion dll built against one release keeps loading
against another. Reusing it here, instead of a second fixture compiled against the dev
source tree, both keeps the generator honest (every call really is 0.5.1 API, never
accidentally a newer member) and exercises that frozen-binding promise directly: booting the
dev build next to a 0.5.1-compiled companion is itself part of what this whole compatibility
question is asking.

## Running it

    VINTAGE_STORY=/path/to/vintagestory dotnet build Manifold.slnx -c Release

    atlas fixture tests/Manifold.Scenarios.CompatFixtures/bin/Release/net10.0/Manifold.Scenarios.CompatFixtures.dll \
      --scenario BuildTheUpgradeFixture \
      --out tests/Manifold.Scenarios.Compat/fixtures/upgrade-from-0.5.1.vcdbs --force

    atlas fixture tests/Manifold.Scenarios.CompatFixtures/bin/Release/net10.0/Manifold.Scenarios.CompatFixtures.dll \
      --scenario BuildTheDowngradeFixture \
      --out tests/Manifold.Scenarios.Compat/fixtures/downgrade-from-dev.vcdbs --force

(`atlas` is the `Pixnop.Atlas.Cli` dotnet tool; `dotnet tool install -g Pixnop.Atlas.Cli
--version 0.15.0` if not already installed, matching `Pixnop.Atlas.XUnit`'s version, since an
older CLI reports version skew and exits 2 instead of harvesting.) Re-run both any time
`CompatFixtureModSystem` or a builder scenario's world-building steps change; commit the
resulting files alongside that change.

Each builder can also be run as an ordinary scenario for iterating on it
(`atlas run ...Fixtures.dll --filter BuildTheUpgradeFixture`, or `dotnet test`): its
assertions on its own setup (the player actually lands where expected, actually gets forced
into Creative, and so on) catch a broken generator before it ever reaches `atlas fixture`.

## What each builder does

Both play the same short script against a single static persistent dimension,
`manicompat:compat` (`LastVisited` spawn behavior, a forced Creative game mode, and a
separate inventory profile all on the one dimension, deliberately: this generator's whole job
is producing every persisted shape `Manifold.Scenarios.Compat` checks, in one place):

1. A player joins, walks to a known spot, and enters `compat`: a first visit, so `LastVisited`
   falls back to the pre-transit coordinates; entry forces Creative, saving Survival.
2. A block the player places (a chest, distinct from the slab worldgen's granite) marks
   "generated terrain, plus something a player actually did to it".
3. The player receives an item that only exists in `compat`'s own separate inventory set.
4. The player moves, then leaves to the overworld: records `LastVisited` at the new spot and
   restores Survival (consuming the pre-forced-mode save from step 1).
5. The player re-enters: lands back at the recorded spot (asserted here too, so a broken
   generator fails loudly instead of only surfacing as a confusing verifier failure) and
   forces Creative again, staying inside for the harvest, so a FRESH pre-forced-mode save
   (Survival) is the one on disk for the other build to restore.

`BuildTheDowngradeFixture` differs only in which Manifold build is staged (the dev one) and
in the player name (`compatdev` vs `compat051`, so the two fixtures' player identities never
collide if ever loaded in the same process).
