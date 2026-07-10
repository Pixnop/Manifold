namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Transit that starts and ends in dimension 0. This is the one transit family a stage 1 Atlas
/// rollback can cover (no mini-dimension chunk is ever loaded and no player joins), so both
/// scenarios adopt RollbackWorld. They deliberately share coordinates: each one asserts those
/// positions are air on entry, which turns the rollback contract itself into a tested property
/// instead of trusted infrastructure. Under shared-world isolation one of the two orderings
/// would fail.
/// </summary>
[Trait("Category", "E2E")]
public class OverworldTransitScenarios : ManifoldScenarioBase
{
    // Rollback-eligible: dimension-0 block writes only, no joined players.
    [AtlasScenario(RollbackWorld = true)]
    public async Task Chest_Should_KeepContents_When_TeleportedWithinOverworld()
    {
        BlockPos source = World.Spawn.Offset(2, 1, 3);
        BlockPos target = World.Spawn.Offset(-3, 1, -2);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());

        World.SetBlock("game:chest-east", source);
        await World.Ticks(2);
        var container = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(source), exactMatch: false);
        var sticks = new ItemStack(World.Api.World.GetItem(new AssetLocation("game", "stick")), 9);
        container.Inventory[0].Itemstack = sticks;
        container.Inventory[0].MarkDirty();
        await World.Ticks(2);

        CommandResult result = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 overworld {target.X} {target.Y} {target.Z}");
        Assert.True(result.Ok, result.Message);
        Assert.Equal("moved", result.Message);

        await World.Until(
            () => World.BlockAt(target).Code?.ToString() == "game:chest-east",
            timeoutTicks: 600);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());

        var arrived = Assert.IsType<IBlockEntityContainer>(
            World.Api.World.BlockAccessor.GetBlockEntity(target), exactMatch: false);
        ItemStack? stack = arrived.Inventory[0].Itemstack;
        Assert.NotNull(stack);
        Assert.Equal(9, stack!.StackSize);
        Assert.Equal("game:stick", stack.Collectible.Code.ToString());
    }

    // Rollback-eligible: reads plus one no-op transit, no joined players. Runs against the same
    // coordinates as the scenario above on purpose; see the class summary.
    [AtlasScenario(RollbackWorld = true)]
    public async Task TeleportBlock_Should_ReportNoOp_When_SourceIsAir()
    {
        BlockPos source = World.Spawn.Offset(-3, 1, -2);
        BlockPos target = World.Spawn.Offset(2, 1, 3);
        Assert.Equal("game:air", World.BlockAt(source).Code.ToString());
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());

        CommandResult result = await World.ExecuteCommand(
            $"/atlasfx teleport-block {source.X} {source.Y} {source.Z} 0 overworld {target.X} {target.Y} {target.Z}");

        Assert.True(result.Ok, result.Message);
        Assert.Equal("no-op", result.Message);
        Assert.Equal("game:air", World.BlockAt(target).Code.ToString());
    }
}
