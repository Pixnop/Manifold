namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Issue #79: since VS 1.22.4 the server validates the pick range of every block-entity packet,
/// and the check refuses outright when the player's dimension differs from the block's. These
/// scenarios drive the chest's real "open inventory" packet handler (packet id 1000, what the
/// client sends on right click) next to a player standing in a Manifold dimension.
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

        CommandResult transit = await World.ExecuteCommand("/atlasfx teleport-player atlas_invdim flat");
        Assert.True(transit.Ok, transit.Message);
        await World.Until(() => player.Position.dimension == flatId, timeoutTicks: 600);

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
