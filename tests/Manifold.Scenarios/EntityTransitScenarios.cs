namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

[Trait("Category", "E2E")]
public class EntityTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
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

    [AtlasScenario]
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
}
