namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

// Strict rollback (see README): nothing here joins players.
[Trait("Category", "E2E")]
public class BlockTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedAcrossDimensions()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(3, 1, 0);
        await PlaceChest(source, "stick", 5);

        CommandResult result = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 520 6 520");

        var target = new BlockPos(520, 6, 520, flatId);
        await BlockBecomes(target, "game:chest-east");

        Assert.True(result.Ok, "TeleportBlock reported failure.");
        Assert.Equal("moved", result.Message);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        AssertChestHolds(target, "stick", 5);
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedBackToOverworld()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(5, 1, 0);
        await PlaceChest(source, "flint", 7);

        await Ok(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 524 6 524");

        var flatPos = new BlockPos(524, 6, 524, flatId);
        await BlockBecomes(flatPos, "game:chest-east");

        // Return leg: source is in the mini-dimension, destination back in dimension 0.
        BlockPos home = World.Spawn.Offset(7, 1, 0);
        CommandResult inbound = await Ok(
            $"/atlasfx teleport-block 524 6 524 {flatId} overworld {home.X} {home.Y} {home.Z}");
        Assert.Equal("moved", inbound.Message);

        await BlockBecomes(home, "game:chest-east");
        Assert.Equal("game:air", World.BlockAt(flatPos).Code.ToString());

        AssertChestHolds(home, "flint", 7);
    }
}
