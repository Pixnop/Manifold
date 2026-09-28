using Atlas.XUnit;
using Xunit;

// Atlas boots one embedded Vintage Story server per process; concurrent scenario
// classes would race on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// No assembly-level [AtlasMods]: BuildTheDowngradeFixture, the only class here, uses the
// assembly-default mod set (the dev build, staged by the ProjectReference AtlasMod sugar below).
// See README.md for why this project exists and how it is run.
