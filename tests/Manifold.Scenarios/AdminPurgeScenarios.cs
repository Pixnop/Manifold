namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

[Trait("Category", "E2E")]
public class AdminPurgeScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Purge_Should_RefuseOverworld_And_Relight_Should_RefuseConsole()
    {
        // The built-in overworld is immutable, even for the admin recovery command.
        CommandResult purge = await World.ExecuteCommand("/manifold purge manifold:overworld");
        Assert.False(purge.Ok, "purging the overworld must be refused");
        Assert.Contains("built-in", purge.Message);

        // Relight is caller-relative; the console has no position, so it must refuse cleanly.
        // The exact wording comes from the engine's caller precondition ("Caller must be
        // player"), with Manifold's own "Players only." as the in-handler backstop.
        CommandResult relight = await World.ExecuteCommand("/manifold relight");
        Assert.False(relight.Ok, "console relight must be refused");
        Assert.Contains("player", relight.Message, StringComparison.OrdinalIgnoreCase);
    }

    [AtlasScenario]
    public async Task PersistentDimension_Should_RefuseTryRemove_And_PurgeAndRecreate_When_Purged()
    {
        CommandResult create = await World.ExecuteCommand("/atlasfx create-persistent ptemp");
        Assert.True(create.Ok, create.Message);
        int firstId = await DimensionId("ptemp");

        // The ordinary removal path refuses a persistent dimension outright.
        CommandResult remove = await World.ExecuteCommand("/atlasfx remove ptemp");
        Assert.False(remove.Ok, "TryRemove on a persistent dimension must throw");
        Assert.Contains("persistent", remove.Message);

        // The admin purge is the documented teardown: it removes, releases the id and fires Destroyed.
        CommandResult purge = await World.ExecuteCommand("/manifold purge atlasfixture:ptemp");
        Assert.True(purge.Ok, purge.Message);
        Assert.Contains("Purged dimension", purge.Message);
        await World.Until(() => FlagIsSet("atlasfixture:event:destroyed:ptemp"), timeoutTicks: 200);

        // The code and the engine id pool are free again: the same code registers anew.
        CommandResult recreate = await World.ExecuteCommand("/atlasfx create-persistent ptemp");
        Assert.True(recreate.Ok, recreate.Message);
        int secondId = await DimensionId("ptemp");
        Assert.InRange(secondId, 10, 1023);
    }

    [AtlasScenario]
    public async Task Purge_Should_EvacuateOccupants_When_DimensionIsOccupied()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer carol = await World.JoinPlayer("atlas_carol");

        CommandResult transit = await World.ExecuteCommand("/atlasfx teleport-player atlas_carol flat");
        Assert.True(transit.Ok, transit.Message);
        await World.Until(() => carol.Position.dimension == flatId, timeoutTicks: 600);

        CommandResult purge = await World.ExecuteCommand("/manifold purge atlasfixture:flat");
        Assert.True(purge.Ok, purge.Message);
        Assert.Contains("evacuated 1 player(s)", purge.Message);

        await World.Until(() => carol.Position.dimension == 0, timeoutTicks: 600);
        await World.Until(() => FlagIsSet("atlasfixture:event:destroyed:flat"), timeoutTicks: 200);
    }
}
