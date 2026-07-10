namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Creation and teardown lifecycles against the real engine: removal refusals, transit refusal
/// after removal, and re-creation under the same code.
/// </summary>
[Trait("Category", "E2E")]
public class DimensionLifecycleScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task PersistentDimension_Should_RefuseRemoval_When_TryRemoveIsUsed()
    {
        await DimensionId("flat");

        CommandResult result = await World.ExecuteCommand("/atlasfx remove flat");

        Assert.False(result.Ok, "TryRemove accepted a persistent dimension.");
        Assert.Contains("DimensionStateException", result.Message);
    }

    // rollback-stage3-candidate: create-ephemeral pregenerates the new dimension's spawn region,
    // so mini-dimension chunks are loaded mid-scenario and stage 1 rollback would degrade to a
    // full recycle; FreshWorld gives the registry mutations a clean host instead. Needs:
    // mini-dimension chunk snapshot/restore AND a rollback story for Manifold's in-memory
    // registry, which is ModSystem state that no world snapshot restores (rolling back SaveGame
    // data alone would desynchronize the registry from its persisted manifest).
    [AtlasScenario(FreshWorld = true)]
    public async Task EphemeralDimension_Should_RefuseTransitThenAllowRecreate_When_Removed()
    {
        CommandResult created = await World.ExecuteCommand("/atlasfx create-ephemeral cycle");
        Assert.True(created.Ok, created.Message);
        int firstId = await DimensionId("cycle");
        Assert.InRange(firstId, 10, 1023);

        CommandResult removed = await World.ExecuteCommand("/atlasfx remove cycle");
        Assert.True(removed.Ok, removed.Message);
        Assert.Equal("removed", removed.Message);

        // Transit into the removed dimension must be refused, not crash the server.
        BlockPos origin = World.Spawn.Offset(3, 1, 3);
        Entity chicken = World.SpawnEntity("game:chicken-hen", origin);
        await World.Ticks(2);
        CommandResult refused = await World.ExecuteCommand(
            $"/atlasfx teleport-entity {chicken.EntityId} cycle");
        Assert.False(refused.Ok, "Transit into a removed dimension was accepted.");
        Assert.Contains("DimensionNotFoundException", refused.Message);

        // The code is free again: re-creating under the same path yields a working dimension.
        CommandResult recreated = await World.ExecuteCommand("/atlasfx create-ephemeral cycle");
        Assert.True(recreated.Ok, recreated.Message);
        int secondId = ReadDimensionId("cycle")!.Value;
        Assert.InRange(secondId, 10, 1023);

        CommandResult transit = await World.ExecuteCommand(
            $"/atlasfx teleport-entity {chicken.EntityId} cycle");
        Assert.True(transit.Ok, transit.Message);
        var arrival = new BlockPos(512, 6, 512, secondId);
        await World.Until(
            () => World.EntitiesIn(arrival.Area(16)).Any(e => e.EntityId == chicken.EntityId),
            timeoutTicks: 600);
    }
}
