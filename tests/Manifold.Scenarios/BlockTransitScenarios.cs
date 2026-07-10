namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

// rollback-stage3-candidate: the moved chests land in the flat mini-dimension, whose loaded
// chunks make stage 1 rollback degrade to a full recycle. Isolation is done with disjoint target
// coordinates per scenario instead. Needs: snapshot/restore of mini-dimension chunk columns
// including block entities.
[Trait("Category", "E2E")]
public class BlockTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Chest_Should_KeepContents_When_TeleportedAcrossDimensions()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(3, 1, 0);
        World.SetBlock("game:chest-east", source);
        await World.Ticks(2);

        var container = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(source), exactMatch: false);
        var sticks = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", "stick")), 5);
        container.Inventory[0].Itemstack = sticks;
        container.Inventory[0].MarkDirty();
        await World.Ticks(2);

        CommandResult result = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 520 6 520");

        var target = new BlockPos(520, 6, 520, flatId);
        await World.Until(
            () => World.BlockAt(target).Code?.ToString() == "game:chest-east",
            timeoutTicks: 600);

        Assert.True(result.Ok, "TeleportBlock reported failure.");
        Assert.Equal("moved", result.Message);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        var arrivedContainer = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(target), exactMatch: false);
        ItemStack? arrivedStack = arrivedContainer.Inventory[0].Itemstack;
        Assert.NotNull(arrivedStack);
        Assert.Equal(5, arrivedStack!.StackSize);
        Assert.Equal("game:stick", arrivedStack.Collectible.Code.ToString());
    }

    [AtlasScenario]
    public async Task Chest_Should_KeepContents_When_TeleportedBackToOverworld()
    {
        int flatId = await DimensionId("flat");

        BlockPos source = World.Spawn.Offset(5, 1, 0);
        World.SetBlock("game:chest-east", source);
        await World.Ticks(2);

        var container = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(source), exactMatch: false);
        var flints = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", "flint")), 7);
        container.Inventory[0].Itemstack = flints;
        container.Inventory[0].MarkDirty();
        await World.Ticks(2);

        CommandResult outbound = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 524 6 524");
        Assert.True(outbound.Ok, outbound.Message);

        var flatPos = new BlockPos(524, 6, 524, flatId);
        await World.Until(
            () => World.BlockAt(flatPos).Code?.ToString() == "game:chest-east",
            timeoutTicks: 600);

        // Return leg: source is in the mini-dimension, destination back in dimension 0.
        BlockPos home = World.Spawn.Offset(7, 1, 0);
        CommandResult inbound = await World.ExecuteCommand(
            $"/atlasfx teleport-block 524 6 524 {flatId} overworld {home.X} {home.Y} {home.Z}");
        Assert.True(inbound.Ok, inbound.Message);
        Assert.Equal("moved", inbound.Message);

        await World.Until(
            () => World.BlockAt(home).Code?.ToString() == "game:chest-east",
            timeoutTicks: 600);
        Assert.Equal("game:air", World.BlockAt(flatPos).Code.ToString());

        var returned = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(home), exactMatch: false);
        ItemStack? stack = returned.Inventory[0].Itemstack;
        Assert.NotNull(stack);
        Assert.Equal(7, stack!.StackSize);
        Assert.Equal("game:flint", stack.Collectible.Code.ToString());
    }
}
