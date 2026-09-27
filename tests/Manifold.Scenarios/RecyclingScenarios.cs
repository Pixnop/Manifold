namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

// Regression for the 0.4.2 fix: destroying a dimension prunes its GeneratedColumnStore markers,
// so a later dimension that reuses its recycled engine id runs its own worldgen instead of
// loading the stale on-disk chunk the previous occupant left behind. DimensionAllocator is
// first-fit and releases immediately, so a lone create/remove/create cycle recycles the id
// deterministically in this fixture.
[Trait("Category", "E2E")]
public class RecyclingScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Dimension_Should_RegenerateFreshTerrain_When_ReusingARecycledEngineId()
    {
        await World.ExecuteCommand("/atlasfx create-ephemeral recyclea");
        int idA = await DimensionId("recyclea");
        var markerInA = new BlockPos(512, 5, 512, idA);
        await BlockBecomes(markerInA, "game:air", timeoutTicks: 1200);

        World.SetBlock("game:rock-andesite", markerInA);
        await World.Ticks(2);
        Assert.Equal("game:rock-andesite", World.BlockAt(markerInA).Code?.ToString());

        await Ok("/atlasfx remove recyclea");

        await World.ExecuteCommand("/atlasfx create-ephemeral recycleb");
        int idB = await DimensionId("recycleb");
        Assert.Equal(idA, idB);

        var probeInB = new BlockPos(512, 5, 512, idB);
        await BlockBecomes(probeInB, "game:air", timeoutTicks: 1200);

        var freshGranite = new BlockPos(512, 4, 512, idB);
        Assert.Equal("game:rock-granite", World.BlockAt(freshGranite).Code?.ToString());
    }
}
