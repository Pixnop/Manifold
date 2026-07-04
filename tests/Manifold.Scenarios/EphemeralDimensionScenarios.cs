namespace Manifold.Scenarios;

using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

[Trait("Category", "E2E")]
public class EphemeralDimensionScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task EphemeralDimension_Should_GenerateAndDisappear_When_CreatedThenRemoved()
    {
        World.ExecuteCommand("/atlasfx create-ephemeral temp1");
        int tempId = await DimensionId("temp1");
        Assert.InRange(tempId, 10, 1023);

        // Runtime-created dimension must produce worldgen output, same as boot-time ones.
        var probe = new BlockPos(512, 3, 512, tempId);
        await World.Until(
            () => World.BlockAt(probe).Code?.ToString() == "game:rock-granite",
            timeoutTicks: 1200);

        World.ExecuteCommand("/atlasfx remove temp1");
        await World.Until(() => FlagIsSet("atlasfixture:removed:temp1"), timeoutTicks: 200);
    }
}
