namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

[Trait("Category", "E2E")]
public class ForceRemoveScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task ForceRemove_Should_EvacuateAndRemove_When_EphemeralIsOccupied()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-ephemeral ftemp");
        Assert.True(create.Ok, create.Message);
        int ftempId = await DimensionId("ftemp");

        ITestPlayer dave = await World.JoinPlayer("atlas_dave");
        CommandResult transit = await World.ExecuteCommand("/atlasfx teleport-player atlas_dave ftemp");
        Assert.True(transit.Ok, transit.Message);
        await World.Until(() => dave.Position.dimension == ftempId, timeoutTicks: 600);

        // PlayerArriving fired between generation and the teleport.
        Assert.True(FlagIsSet("atlasfixture:event:player-arriving:ftemp"), "PlayerArriving did not fire");

        // TryRemove refuses while a player is inside.
        CommandResult remove = await World.ExecuteCommand("/atlasfx remove ftemp");
        Assert.True(remove.Ok, remove.Message);
        Assert.Equal("not-removed", remove.Message);

        // ForceRemoveDimension evacuates first, then removes (or the transit-out reap beats it; both succeed).
        CommandResult force = await World.ExecuteCommand("/atlasfx force-remove ftemp");
        Assert.True(force.Ok, force.Message);
        Assert.Equal("removed", force.Message);

        await World.Until(() => dave.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => FlagIsSet("atlasfixture:event:destroyed:ftemp"), timeoutTicks: 200);
    }

    [AtlasScenario]
    public async Task ForceRemove_Should_Refuse_When_DimensionIsPersistent()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-persistent fperm");
        Assert.True(create.Ok, create.Message);
        await DimensionId("fperm");

        // ForceRemoveDimension only tears down ephemerals; persistent falls through to
        // TryRemove's canonical refusal, without evacuating anyone.
        CommandResult force = await World.ExecuteCommand("/atlasfx force-remove fperm");
        Assert.False(force.Ok, "force-remove of a persistent dimension must be refused");
        Assert.Contains("persistent", force.Message);
    }

    [AtlasScenario]
    public async Task EphemeralDimension_Should_AutoReap_When_LastPlayerTransitsOut()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-ephemeral reap1");
        Assert.True(create.Ok, create.Message);
        int reapId = await DimensionId("reap1");

        ITestPlayer erin = await World.JoinPlayer("atlas_erin");
        CommandResult enter = await World.ExecuteCommand("/atlasfx teleport-player atlas_erin reap1");
        Assert.True(enter.Ok, enter.Message);
        await World.Until(() => erin.Position.dimension == reapId, timeoutTicks: 600);

        // Transiting the last occupant out leaves the ephemeral empty: it reaps itself.
        CommandResult leave = await World.ExecuteCommand("/atlasfx teleport-player atlas_erin overworld");
        Assert.True(leave.Ok, leave.Message);
        await World.Until(() => erin.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => FlagIsSet("atlasfixture:event:destroyed:reap1"), timeoutTicks: 600);
    }
}
