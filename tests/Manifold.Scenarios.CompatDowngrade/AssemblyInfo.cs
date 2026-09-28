using Atlas.XUnit;
using Xunit;

// Atlas boots one embedded Vintage Story server per process; concurrent scenario
// classes would race on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// No assembly-level [AtlasMods]: the one class here (DowngradeVerifyScenarios) always overrides
// with the 0.5.1 release zip explicitly (see its [AtlasWorld]). See README.md.
