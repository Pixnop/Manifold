namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Terrain probes pregenerate their dimension explicitly (/atlasfx pregen) because the
/// fixture no longer generates any mini-dimension region at boot; see the fixture mod.
/// </summary>
[Trait("Category", "E2E")]
public class DimensionWorldgenScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Dimensions_Should_GetDistinctModIds_When_Registered()
    {
        int flatId = await DimensionId("flat");
        int voidId = await DimensionId("void");

        Assert.InRange(flatId, 10, 1023);
        Assert.InRange(voidId, 10, 1023);
        Assert.NotEqual(flatId, voidId);
    }

    [AtlasScenario]
    public async Task FlatDimension_Should_GenerateGraniteSlab_When_Pregenerated()
    {
        int flatId = await DimensionId("flat");
        var inSlab = new BlockPos(512, 3, 512, flatId);
        var aboveSlab = new BlockPos(512, 10, 512, flatId);

        CommandResult pregen = await World.ExecuteCommand("/atlasfx pregen flat");
        Assert.True(pregen.Ok, pregen.Message);

        await World.Until(
            () => World.BlockAt(inSlab).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        Assert.Equal("game:air", World.BlockAt(aboveSlab).Code.ToString());
    }

    [AtlasScenario]
    public async Task Dimensions_Should_HaveIsolatedWorldgen_When_SharingCoordinates()
    {
        int flatId = await DimensionId("flat");
        int voidId = await DimensionId("void");
        var probeFlat = new BlockPos(512, 3, 512, flatId);
        var probeVoid = new BlockPos(512, 3, 512, voidId);

        Assert.True((await World.ExecuteCommand("/atlasfx pregen flat")).Ok);
        Assert.True((await World.ExecuteCommand("/atlasfx pregen void")).Ok);

        await World.Until(
            () => World.BlockAt(probeFlat).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        // Same local coordinates, different dimension, different worldgen output.
        Assert.Equal("game:air", World.BlockAt(probeVoid).Code.ToString());
    }
}
