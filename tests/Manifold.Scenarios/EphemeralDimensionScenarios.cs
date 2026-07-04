namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

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
