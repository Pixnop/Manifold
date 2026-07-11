namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

// rollback-stage2-candidate: joined test players hard-refuse stage 1 rollback. The interesting
// rollback surface here is player moddata: Manifold persists the swapped-out inventory sets in
// moddata, so a rollback that restored world state but not moddata would resurrect items in two
// places. Needs: per-player moddata capture in the same snapshot as SaveGame data.
[Trait("Category", "E2E")]
public class PlayerInventoryScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Inventory_Should_SwapWithoutLoss_When_EnteringSeparateInventoryDimension()
    {
        int vaultId = await DimensionId("vault");
        ITestPlayer player = await World.JoinPlayer("atlasvaulter");
        await player.GiveItem("game:stick", 5);
        Assert.Equal(5, HotbarCount(player, "game:stick"));

        await World.ExecuteCommand("/atlasfx teleport-player atlasvaulter vault");
        await World.Until(() => player.Position.dimension == vaultId, timeoutTicks: 600);
        await World.Until(() => HotbarCount(player, "game:stick") == 0, timeoutTicks: 200);

        await player.GiveItem("game:flint", 3);
        Assert.Equal(3, HotbarCount(player, "game:flint"));

        await World.ExecuteCommand("/atlasfx teleport-player atlasvaulter overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => HotbarCount(player, "game:stick") == 5, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:flint"));

        await World.ExecuteCommand("/atlasfx teleport-player atlasvaulter vault");
        await World.Until(() => player.Position.dimension == vaultId, timeoutTicks: 600);
        await World.Until(() => HotbarCount(player, "game:flint") == 3, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:stick"));
    }
}
