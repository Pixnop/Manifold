namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// A class of its own so that no player has joined before the scenario runs: with nobody near, the
/// overworld map chunk under the fixture dimensions is not loaded and the engine drops block-light
/// work for those columns. RelightRegion must then keep the light sources pending and light them
/// once a player arrives and the engine loads that overworld column by itself.
/// </summary>
[Trait("Category", "E2E")]
public class RelightPendingScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task RelightRegion_Should_LightPendingSources_When_APlayerArrivesLater()
    {
        await Ok("/atlasfx2 create-darksky relight_pending 40");
        int dimId = await DimensionId("relight_pending");
        var lantern = new BlockPos(520, 6, 520, dimId);
        var beside = new BlockPos(521, 6, 520, dimId);
        await BlockBecomes(new BlockPos(520, 3, 520, dimId), "game:rock-granite", timeoutTicks: 1200);
        Assert.Null(World.Api.WorldManager.GetMapChunk(16, 16));

        int lanternId = World.Api.World.GetBlock(new AssetLocation("game:paperlantern-on"))!.BlockId;
        World.Api.World.GetBlockAccessor(synchronize: false, relight: false, strict: false).SetBlock(lanternId, lantern);

        await Ok("/atlasfx2 relight-region relight_pending 520 6 520 520 6 520");
        await Task.Delay(TimeSpan.FromSeconds(1));
        await World.Ticks(5);
        Assert.Equal(0, BlockLight(beside));

        ITestPlayer player = await JoinSurvivalPlayer("relightlate");
        await Ok("/atlasfx teleport-player relightlate relight_pending");
        await LandedAt(player, dimId, 512, 512);

        await World.Until(() => BlockLight(beside) == 20, timeoutTicks: 1200);
    }

    private int BlockLight(BlockPos pos) =>
        World.Api.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight);
}
