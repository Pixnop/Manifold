namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

// rollback-stage3-candidate: create-ephemeral generates the new dimension's spawn region, so
// mini-dimension chunks are loaded mid-scenario and stage 1 rollback would degrade to a full
// recycle. On top of chunk columns, a rollback that covered this class would also need to restore
// Manifold's in-memory registry (ModSystem state), which even a stage 3 world snapshot will not
// do; SaveGame data alone rolling back would desynchronize the registry from its manifest.
[Trait("Category", "E2E")]
public class EphemeralDimensionScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task EphemeralDimension_Should_GenerateAndDisappear_When_CreatedThenRemoved()
    {
        CommandResult createResult = await World.ExecuteCommand("/atlasfx create-ephemeral temp1");
        Assert.True(createResult.Ok, "create-ephemeral reported failure.");
        int tempId = await DimensionId("temp1");
        Assert.InRange(tempId, 10, 1023);

        // Runtime-created dimension must produce worldgen output, same as boot-time ones.
        var probe = new BlockPos(512, 3, 512, tempId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        CommandResult removeResult = await World.ExecuteCommand("/atlasfx remove temp1");
        Assert.True(removeResult.Ok, "remove reported failure.");
        Assert.Equal("removed", removeResult.Message);
    }
}
