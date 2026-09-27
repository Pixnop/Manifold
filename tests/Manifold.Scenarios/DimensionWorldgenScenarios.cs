namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Terrain probes pregenerate their dimension explicitly (/atlasfx pregen) because the
/// fixture no longer generates any mini-dimension region at boot, except "pregenerated"
/// (see below), which is the deliberate exception exercising IManifoldServer.GenerateRegion.
/// </summary>
[Trait("Category", "E2E")]
public class DimensionWorldgenScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Pregenerated_Should_HaveGraniteAtSpawn_When_NoPlayerEverTransited()
    {
        int pregenId = await DimensionId("pregenerated");

        // A real joined player, present the whole time but never sent anywhere: proves the terrain
        // below did not come from a transit, only from the GenerateRegion call the fixture makes
        // right after RegisterStatic, with no player involved (issue #69).
        ITestPlayer player = await World.JoinPlayer("atlas_pregen");
        await World.Ticks(2);

        var spawnColumn = new BlockPos(512, 3, 512, pregenId);
        Assert.Equal("game:rock-granite", World.BlockAt(spawnColumn).Code?.ToString());
        Assert.Equal(0, player.Position.dimension);
    }

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

        await Ok("/atlasfx pregen flat");

        await BlockBecomes(inSlab, "game:rock-granite", timeoutTicks: 1200);

        Assert.Equal("game:air", World.BlockAt(aboveSlab).Code.ToString());
    }

    [AtlasScenario]
    public async Task Dimensions_Should_HaveIsolatedWorldgen_When_SharingCoordinates()
    {
        int flatId = await DimensionId("flat");
        int voidId = await DimensionId("void");
        var probeFlat = new BlockPos(512, 3, 512, flatId);
        var probeVoid = new BlockPos(512, 3, 512, voidId);

        await Ok("/atlasfx pregen flat");
        await Ok("/atlasfx pregen void");

        await BlockBecomes(probeFlat, "game:rock-granite", timeoutTicks: 1200);

        // Same local coordinates, different dimension, different worldgen output.
        Assert.Equal("game:air", World.BlockAt(probeVoid).Code.ToString());
    }
}
