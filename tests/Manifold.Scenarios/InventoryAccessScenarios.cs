namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Issue #79: VS 1.22.4 added an interaction range check to every block-entity packet. In 1.22.4
/// and 1.22.5 it measured the player's eye with the internal Y (local Y plus 32768 per dimension)
/// against the block's local Y, so it failed in every dimension but the overworld; 1.22.6 fixed
/// it (same-dimension test first, then local coordinates on both sides). These scenarios drive
/// the chest's real "open inventory" packet handler (packet id 1000, what the client sends on
/// right click) next to a player standing in a Manifold dimension: they fail on a 1.22.5 server
/// and pass on 1.22.3 and from 1.22.6 on.
/// </summary>
[Trait("Category", "E2E")]
public class InventoryAccessScenarios : ManifoldScenarioBase
{
    private const int OpenInventoryPacket = 1000;

    [AtlasScenario]
    public async Task Chest_Should_Open_When_PlayerStandsNextToItInTheOverworld()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_invctl");
        await AssertChestOpensNextTo(player);
    }

    [AtlasScenario]
    public async Task Chest_Should_Open_When_PlayerStandsNextToItInACustomDimension()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_invdim");

        await Ok("/atlasfx teleport-player atlas_invdim flat");

        // ChangeDimension flips the dimension at once, but the X/Z teleport lands a few ticks later,
        // once its chunks load: wait for the fixture's spawn column, not just the dimension, or the
        // chest goes into an ungenerated column at the old overworld coordinates.
        await World.Until(
            () => player.Position.dimension == flatId
                && Math.Abs(player.Position.X - 512) <= 1
                && Math.Abs(player.Position.Z - 512) <= 1,
            timeoutTicks: 600);

        await AssertChestOpensNextTo(player);
    }

    private async Task AssertChestOpensNextTo(ITestPlayer player)
    {
        await World.Ticks(5);
        BlockPos feet = player.Position;
        var chestPos = new BlockPos(feet.X + 1, feet.Y, feet.Z, feet.dimension);

        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);

        BlockEntity be = World.Api.World.BlockAccessor.GetBlockEntity(chestPos)!;
        var container = Assert.IsType<IBlockEntityContainer>(be, exactMatch: false);
        Assert.Equal(feet.dimension, be.Pos.dimension);

        be.OnReceivedClientPacket(player.Player, OpenInventoryPacket, Array.Empty<byte>());

        string why = $"chest at {chestPos} refused to open for a player at {feet}";
        Assert.True(container.Inventory.HasOpened(player.Player), why);
    }
}
