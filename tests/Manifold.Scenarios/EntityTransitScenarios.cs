namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

// RollbackWorld (Atlas 0.8.0): mini-dimension chunk columns and their chunk-stored entities are
// part of the snapshot since rollback stage 3, and Manifold resyncs its in-memory registry and
// stores from the restored SaveGame through the atlas:rollback:restored hook
// (ManifoldModSystem.OnAtlasRollbackRestored). Strict: nothing in this class joins players or
// otherwise legitimately degrades the rollback, so a degrade is a regression and must fail.
[Trait("Category", "E2E")]
public class EntityTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Entity_Should_ArriveInTargetDimension_When_Teleported()
    {
        int flatId = await DimensionId("flat");

        BlockPos origin = World.Spawn.Offset(2, 1, 2);
        Entity chicken = World.SpawnEntity("game:chicken-hen", origin);
        chicken.WatchedAttributes.SetString("atlasfixture-marker", "kept-across-transit");
        await World.Ticks(2);

        CommandResult result = await World.ExecuteCommand($"/atlasfx teleport-entity {chicken.EntityId} flat");
        Assert.True(result.Ok, "teleport-entity reported failure.");

        var arrival = new BlockPos(512, 6, 512, flatId);
        await World.Until(
            () => World.EntitiesIn(arrival.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);

        Entity arrived = World.EntitiesIn(arrival.Area(16)).Single(e => e.EntityId == chicken.EntityId);
        Assert.True(arrived.Alive, "Entity died during transit.");
        Assert.Equal(flatId, arrived.Pos.Dimension);
        Assert.Equal("kept-across-transit", arrived.WatchedAttributes.GetString("atlasfixture-marker"));
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Entity_Should_LeaveSourceDimension_When_Teleported()
    {
        int flatId = await DimensionId("flat");

        BlockPos origin = World.Spawn.Offset(4, 1, 4);
        Entity chicken = World.SpawnEntity("game:chicken-hen", origin);
        await World.Ticks(2);

        CommandResult result = await World.ExecuteCommand($"/atlasfx teleport-entity {chicken.EntityId} flat");
        Assert.True(result.Ok, "teleport-entity reported failure.");

        var arrival = new BlockPos(512, 6, 512, flatId);
        await World.Until(
            () => World.EntitiesIn(arrival.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);

        // The dimension-0 query at the origin must no longer see the entity.
        Assert.DoesNotContain(
            World.EntitiesIn(origin.Area(16)),
            e => e.EntityId == chicken.EntityId);
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Entity_Should_ReturnToOverworld_When_TeleportedBack()
    {
        int flatId = await DimensionId("flat");

        BlockPos origin = World.Spawn.Offset(6, 1, 6);
        Entity chicken = World.SpawnEntity("game:chicken-hen", origin);
        chicken.WatchedAttributes.SetString("atlasfixture-marker", "round-trip");
        await World.Ticks(2);

        CommandResult toFlat = await World.ExecuteCommand($"/atlasfx teleport-entity {chicken.EntityId} flat");
        Assert.True(toFlat.Ok, toFlat.Message);
        var flatArrival = new BlockPos(512, 6, 512, flatId);
        await World.Until(
            () => World.EntitiesIn(flatArrival.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);

        // Back to dimension 0: the fixture lands overworld transits at the vanilla default spawn.
        CommandResult back = await World.ExecuteCommand($"/atlasfx teleport-entity {chicken.EntityId} overworld");
        Assert.True(back.Ok, back.Message);
        await World.Until(
            () => World.EntitiesIn(World.Spawn.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);

        Entity returned = World.EntitiesIn(World.Spawn.Area(16)).Single(e => e.EntityId == chicken.EntityId);
        Assert.True(returned.Alive, "Entity died during the round trip.");
        Assert.Equal(0, returned.Pos.Dimension);
        Assert.Equal("round-trip", returned.WatchedAttributes.GetString("atlasfixture-marker"));

        // And the flat-dimension query must no longer see it.
        Assert.DoesNotContain(
            World.EntitiesIn(flatArrival.Area(16)),
            e => e.EntityId == chicken.EntityId);
    }
}
