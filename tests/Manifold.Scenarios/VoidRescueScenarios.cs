namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

[Trait("Category", "E2E")]
public class VoidRescueScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Player_Should_BeRescuedToOverworld_When_SavedDimensionNoLongerExists()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-ephemeral doomed");
        Assert.True(create.Ok, create.Message);
        int doomedId = await DimensionId("doomed");

        ITestPlayer eve = await World.JoinPlayer("atlas_eve");
        CommandResult transit = await World.ExecuteCommand("/atlasfx teleport-player atlas_eve doomed");
        Assert.True(transit.Ok, transit.Message);
        await World.Until(() => eve.Position.dimension == doomedId, timeoutTicks: 600);

        // Kick the player while inside: disconnecting does NOT reap the ephemeral, so the
        // saved position keeps pointing at it.
        CommandResult kick = await World.ExecuteCommand("/atlasfx kick atlas_eve");
        Assert.True(kick.Ok, kick.Message);
        await World.Until(() => !eve.IsConnected, timeoutTicks: 600);

        // With the player offline the dimension has no connected occupant: plain removal works.
        CommandResult remove = await World.ExecuteCommand("/atlasfx remove doomed");
        Assert.True(remove.Ok, remove.Message);
        Assert.Equal("removed", remove.Message);

        // Rejoining with a saved position inside the now-gone dimension must not load into the
        // void: the rescue (deferred to PlayerNowPlaying) sends the player to the overworld.
        ITestPlayer eveAgain = await World.JoinPlayer("atlas_eve");
        await World.Until(() => eveAgain.Position.dimension == 0, timeoutTicks: 600);
        Assert.True(eveAgain.IsConnected);
    }
}
