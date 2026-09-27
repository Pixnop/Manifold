namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

// rollback-stage2-candidate: joined test players hard-refuse stage 1 rollback with an
// AtlasSetupException (player entity state is not captured). A stage 2 rollback would also give
// the event-flag assertions real isolation: the atlasfixture:event:* SaveGame flags accumulate
// across scenarios on a shared host, so today only the first scenario asserting a given flag is
// meaningful. Needs: player position/inventory/stats capture plus the existing SaveGame restore.
[Trait("Category", "E2E")]
public class PlayerTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Player_Should_RoundTripAcrossDimensions_When_Teleported()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlastester");
        await World.Ticks(2);

        CommandResult toFlat = await World.ExecuteCommand("/atlasfx teleport-player atlastester flat");
        Assert.True(toFlat.Ok, "teleport-player reported failure.");
        await World.Until(() => player.Position.dimension == flatId, timeoutTicks: 600);
        Assert.True(player.Stats.Health > 0, "Player died during transit.");
        Assert.True(FlagIsSet("atlasfixture:event:player-entered:flat"), "PlayerEntered event did not fire for the target dimension.");
        Assert.True(FlagIsSet("atlasfixture:event:player-left:overworld"), "PlayerLeft event did not fire for the source dimension.");

        CommandResult toOverworld = await World.ExecuteCommand("/atlasfx teleport-player atlastester overworld");
        Assert.True(toOverworld.Ok, "teleport-player reported failure.");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.True(FlagIsSet("atlasfixture:event:player-left:flat"), "PlayerLeft event did not fire on the way back.");
    }
}
