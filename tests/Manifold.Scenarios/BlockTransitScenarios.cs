namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

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

        var container = Assert.IsAssignableFrom<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(source));
        var sticks = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", "stick")), 5);
        container.Inventory[0].Itemstack = sticks;
        container.Inventory[0].MarkDirty();
        await World.Ticks(2);

        World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 flat 520 6 520");

        var target = new BlockPos(520, 6, 520, flatId);
        await World.Until(
            () => World.BlockAt(target).Code?.ToString() == "game:chest-east",
            timeoutTicks: 600);

        Assert.True(FlagIsSet("atlasfixture:result:teleport-block"), "TeleportBlock reported failure.");
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        var arrivedContainer = Assert.IsAssignableFrom<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(target));
        ItemStack? arrivedStack = arrivedContainer.Inventory[0].Itemstack;
        Assert.NotNull(arrivedStack);
        Assert.Equal(5, arrivedStack!.StackSize);
        Assert.Equal("game:stick", arrivedStack.Collectible.Code.ToString());
    }
}
