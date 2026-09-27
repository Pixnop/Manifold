namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

// Scenarios join players, so none can roll back (rollback-stage2-candidate); each uses its own
// player name and dimension path and clears the shared event log first for order independence.
[Trait("Category", "E2E")]
public class TransitEventScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Transit_Should_LogFullSequence_When_PlayerEntersDimension()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("evt_flat");
        await World.Ticks(2);
        ClearEventLog();

        await Ok("/atlasfx teleport-player evt_flat flat");
        await LandedAt(player, flatId, 512, 512);

        Assert.Equal(
            new[] { "entering:flat", "arriving:flat", "left:overworld->flat", "entered:flat" },
            EventLog());
    }

    [AtlasScenario]
    public async Task Transit_Should_VetoBeforeGeneration_When_TargetIsLocked()
    {
        int lockedId = await DimensionId("locked");
        ITestPlayer player = await World.JoinPlayer("evt_locked");
        await World.Ticks(2);
        ClearEventLog();

        await Ok("/atlasfx teleport-player evt_locked locked");

        Assert.Equal(0, player.Position.dimension);
        Assert.Equal(new[] { "entering:locked" }, EventLog());

        // PlayerEntering (and its veto) runs before EnsureRegion: the spawn column was never generated.
        var spawnColumn = new BlockPos(512, 3, 512, lockedId);
        Assert.NotEqual("game:rock-granite", World.BlockAt(spawnColumn).Code?.ToString());
    }

    [AtlasScenario]
    public async Task Transit_Should_VetoAfterGeneration_When_TargetIsGate()
    {
        int gateId = await DimensionId("gate");
        ITestPlayer player = await World.JoinPlayer("evt_gate");
        await World.Ticks(2);
        ClearEventLog();

        await Ok("/atlasfx teleport-player evt_gate gate");

        Assert.Equal(0, player.Position.dimension);
        Assert.Equal(new[] { "entering:gate", "arriving:gate" }, EventLog());

        // PlayerArriving runs after EnsureRegion: the veto is too late to stop generation.
        var spawnColumn = new BlockPos(512, 3, 512, gateId);
        await BlockBecomes(spawnColumn, "game:rock-granite");
    }

    [AtlasScenario]
    public async Task Transit_Should_ContinueThroughSubsequentSubscribers_When_OneThrows()
    {
        int faultyId = await DimensionId("faulty");
        ITestPlayer player = await World.JoinPlayer("evt_faulty");
        await World.Ticks(2);
        ClearEventLog();

        await Ok("/atlasfx teleport-player evt_faulty faulty");
        await LandedAt(player, faultyId, 512, 512);

        Assert.Equal(
            new[] { "entering:faulty", "arriving:faulty", "left:overworld->faulty", "entered:faulty" },
            EventLog());
    }

    [AtlasScenario]
    public async Task Transit_Should_LogEntityMove_When_NonPlayerEntityTeleported()
    {
        Entity chicken = World.SpawnEntity("game:chicken-hen", World.Spawn.Offset(8, 1, 8));
        await World.Ticks(2);
        ClearEventLog();

        await Ok($"/atlasfx teleport-entity {chicken.EntityId} flat");

        Assert.Equal(new[] { "entity:overworld->flat" }, EventLog());
    }

    [AtlasScenario]
    public async Task Registry_Should_LogCreatedThenDestroyed_When_EphemeralDimensionIsRemoved()
    {
        ClearEventLog();

        await Ok("/atlasfx create-ephemeral evtephemeral");

        CommandResult removed = await Ok("/atlasfx remove evtephemeral");
        Assert.Equal("removed", removed.Message);

        Assert.Equal(new[] { "created:evtephemeral", "destroyed:evtephemeral" }, EventLog());
    }
}
