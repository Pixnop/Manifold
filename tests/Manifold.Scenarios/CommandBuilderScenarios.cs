namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// DimensionCommandBuilder-registered commands (/atlasgo, /atlasgoadmin), run as the player rather
/// than the console so privilege checks see the caller's real grants. A joined test player rides
/// the highest-privilege role by default; SetRole("suplayer") downgrades to chat-only first.
/// </summary>
[Trait("Category", "E2E")]
public class CommandBuilderScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task AtlasGo_Should_TeleportToAnchor_When_RunByAPlainPlayer()
    {
        int anchorId = await DimensionId("anchor");
        ITestPlayer player = await World.JoinPlayer("atlas_cmdgo");
        player.Player.SetRole("suplayer");

        CommandResult result = await player.ExecuteCommand("/atlasgo");

        Assert.True(result.Ok, result.Message);
        Assert.Contains("Teleported to", result.Message);
        await LandedAt(player, anchorId, 512, 512);
    }

    [AtlasScenario]
    public async Task AtlasGoAdmin_Should_BeRefused_When_RunByAPlainPlayer()
    {
        ITestPlayer player = await World.JoinPlayer("atlas_cmdgoadmin");
        player.Player.SetRole("suplayer");

        CommandResult result = await player.ExecuteCommand("/atlasgoadmin");

        Assert.False(result.Ok, "A plain player was allowed to run the controlserver-only command.");
        Assert.Equal(0, player.Position.dimension);
    }
}
