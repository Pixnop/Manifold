namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// Dimension teardown against the real engine: forced eviction, refusal paths, and the automatic
/// reap of an ephemeral dimension once its last occupant leaves.
/// </summary>
[Trait("Category", "E2E")]
public class TeardownScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task ForceRemoveDimension_Should_EvacuateThenRemove_When_EphemeralIsOccupied()
    {
        await Ok("/atlasfx create-ephemeral td_evac");
        int evacId = await DimensionId("td_evac");

        ITestPlayer player = await World.JoinPlayer("td_evacp");
        await Ok("/atlasfx teleport-player td_evacp td_evac");
        await LandedAt(player, evacId, 512, 512);

        ClearEventLog();
        CommandResult removed = await Ok("/atlasfx2 force-remove td_evac");
        Assert.Equal("removed", removed.Message);

        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Contains("destroyed:td_evac", EventLog());
    }

    [AtlasScenario]
    public async Task ForceRemoveDimension_Should_RefuseWithoutMovingAnyone_When_DimensionIsPersistent()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("td_persist");
        await Ok("/atlasfx teleport-player td_persist flat");
        await LandedAt(player, flatId, 512, 512);
        var before = player.Position;

        CommandResult refused = await World.ExecuteCommand("/atlasfx2 force-remove flat");
        Assert.False(refused.Ok, "ForceRemoveDimension accepted a persistent dimension.");
        Assert.Contains("DimensionStateException", refused.Message);

        Assert.Equal(before, player.Position);
    }

    [AtlasScenario]
    public async Task TryRemove_Should_ReturnNotRemoved_When_PlayerStandsInsideEphemeral()
    {
        await Ok("/atlasfx create-ephemeral td_occ");
        int occId = await DimensionId("td_occ");

        ITestPlayer player = await World.JoinPlayer("td_occp");
        await Ok("/atlasfx teleport-player td_occp td_occ");
        await LandedAt(player, occId, 512, 512);

        CommandResult remove = await Ok("/atlasfx remove td_occ");
        Assert.Equal("not-removed", remove.Message);
    }

    [AtlasScenario]
    public async Task EphemeralDimension_Should_ReapAutomatically_When_LastOccupantTransitsOut()
    {
        await Ok("/atlasfx create-ephemeral td_reap");
        int reapId = await DimensionId("td_reap");

        ITestPlayer player = await World.JoinPlayer("td_reapp");
        await Ok("/atlasfx teleport-player td_reapp td_reap");
        await LandedAt(player, reapId, 512, 512);

        ClearEventLog();
        await Ok("/atlasfx teleport-player td_reapp overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        Assert.Contains("destroyed:td_reap", EventLog());
        CommandResult state = await World.ExecuteCommand("/atlasfx state td_reap");
        Assert.False(state.Ok, "The reaped ephemeral dimension is still registered.");
        Assert.Equal("unregistered", state.Message);
    }

    [AtlasScenario]
    public async Task EphemeralDimension_Should_NotReap_When_AnotherOccupantRemains()
    {
        await Ok("/atlasfx create-ephemeral td_stay");
        int stayId = await DimensionId("td_stay");

        ITestPlayer alice = await World.JoinPlayer("td_staya");
        ITestPlayer bob = await World.JoinPlayer("td_stayb");
        await Ok("/atlasfx teleport-player td_staya td_stay");
        await Ok("/atlasfx teleport-player td_stayb td_stay");
        await LandedAt(alice, stayId, 512, 512);
        await LandedAt(bob, stayId, 512, 512);

        ClearEventLog();
        await Ok("/atlasfx teleport-player td_staya overworld");
        await World.Until(() => alice.Position.dimension == 0, timeoutTicks: 600);

        Assert.DoesNotContain("destroyed:td_stay", EventLog());
        Assert.Equal(stayId, bob.Position.dimension);
        CommandResult state = await Ok("/atlasfx state td_stay");
        Assert.Equal($"Active:{stayId}", state.Message);
    }
}
