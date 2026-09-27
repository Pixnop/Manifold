namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// IManifoldServer.GetPlayersIn, IDimensionRegistry.GetDimensionOf, and the public
/// ITransitionService.TryTeleportPlayer, driven through /atlasfx2. Each scenario uses its own
/// dimension or player names so shared-world ordering within this class cannot cross-contaminate
/// another scenario's assertions.
/// </summary>
[Trait("Category", "E2E")]
public class QueryScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task TryTeleportPlayer_Should_ReturnTrue_When_TransitSucceeds()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_ttpok");

        CommandResult result = await Ok("/atlasfx2 try-teleport-player atlas_ttpok flat");

        Assert.Equal("moved", result.Message);
        await LandedAt(player, flatId, 512, 512);
    }

    [AtlasScenario]
    public async Task TryTeleportPlayer_Should_ReturnFalse_When_TargetVetoesEntering()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_ttpveto");

        CommandResult result = await Ok("/atlasfx2 try-teleport-player atlas_ttpveto locked");

        Assert.Equal("cancelled", result.Message);
        Assert.Equal(0, player.Position.dimension);
    }
}
