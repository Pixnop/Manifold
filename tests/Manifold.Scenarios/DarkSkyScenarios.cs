namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

[Trait("Category", "E2E")]
public class DarkSkyScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task DarkSky_Should_CapEveryGeneratedColumn_When_CeilingConfigured()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-darksky dark1 60");
        Assert.True(create.Ok, create.Message);
        int darkId = await DimensionId("dark1");

        // Worldgen ran: the granite slab is present at the spawn column.
        var slabProbe = new BlockPos(512, 3, 512, darkId);
        await World.Until(
            () => World.BlockAt(slabProbe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        // The opaque cap seals the configured ceiling on every generated column,
        // not just the spawn one.
        var capAtSpawn = new BlockPos(512, 60, 512, darkId);
        var capNeighbour = new BlockPos(500, 60, 500, darkId);
        await World.Until(() => World.BlockAt(capAtSpawn).BlockId != 0, timeoutTicks: 600);
        Assert.NotEqual(0, World.BlockAt(capNeighbour).BlockId);

        // Between the slab and the cap the column stays open air.
        Assert.Equal(0, World.BlockAt(new BlockPos(512, 30, 512, darkId)).BlockId);
    }
}
