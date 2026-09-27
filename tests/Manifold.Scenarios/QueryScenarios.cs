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
    public async Task GetPlayersIn_Should_ReturnOnlyOccupantsOfThatDimension()
    {
        int vaultId = await DimensionId("vault");
        ITestPlayer inVault = await World.JoinPlayer("atlas_qpin");
        ITestPlayer inOverworld = await World.JoinPlayer("atlas_qpout");

        await Ok("/atlasfx teleport-player atlas_qpin vault");
        await LandedAt(inVault, vaultId, 512, 512);

        CommandResult vaultResult = await Ok("/atlasfx2 players-in vault");
        Assert.Equal("atlas_qpin", vaultResult.Message);

        CommandResult overworldResult = await Ok("/atlasfx2 players-in overworld");
        var overworldNames = overworldResult.Message.Split(',');
        Assert.Contains("atlas_qpout", overworldNames);
        Assert.DoesNotContain("atlas_qpin", overworldNames);
    }

    [AtlasScenario]
    public async Task GetDimensionOf_Should_ReturnOverworld_When_PlayerNeverTransited()
    {
        await World.JoinPlayer("atlas_qdow");

        CommandResult result = await Ok("/atlasfx2 dimension-of atlas_qdow");

        Assert.Equal("manifold:overworld", result.Message);
    }

    [AtlasScenario]
    public async Task GetDimensionOf_Should_ReturnTargetDimension_After_Transit()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_qdflat");

        await Ok("/atlasfx teleport-player atlas_qdflat flat");
        await LandedAt(player, flatId, 512, 512);

        CommandResult result = await Ok("/atlasfx2 dimension-of atlas_qdflat");

        Assert.Equal("atlasfixture:flat", result.Message);
    }

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
