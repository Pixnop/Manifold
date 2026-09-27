namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

// Scenarios share one world (no joined-player class can roll back or restart): every scenario
// below uses its own player names and dimension codes so order never matters.
[Trait("Category", "E2E")]
public class AdminCommandScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Purge_Should_RefuseBuiltInOverworld_When_Purged()
    {
        CommandResult result = await World.ExecuteCommand("/manifold purge manifold:overworld");
        Assert.False(result.Ok, "Purging the built-in overworld was accepted.");
        Assert.Contains("built-in", result.Message);
    }

    [AtlasScenario]
    public async Task Purge_Should_Error_When_CodeIsUnknown()
    {
        CommandResult result = await World.ExecuteCommand("/manifold purge atlasfixture:no-such-dim");
        Assert.False(result.Ok, "Purging an unregistered code was accepted.");
        Assert.Contains("No dimension registered", result.Message);
    }

    [AtlasScenario]
    public async Task Purge_Should_TearDownPersistentDimension_When_AtlasfxRemoveRefuses()
    {
        CommandResult created = await Ok("/atlasfx create-persistent adminpurge1");

        // The plain remove path refuses a runtime Persistent dimension outright: only purge tears it down.
        CommandResult removeRefused = await World.ExecuteCommand("/atlasfx remove adminpurge1");
        Assert.False(removeRefused.Ok, "/atlasfx remove accepted a persistent dimension.");
        Assert.Contains("DimensionStateException", removeRefused.Message);

        ClearEventLog();
        await Ok("/manifold purge atlasfixture:adminpurge1");
        Assert.Contains("destroyed:adminpurge1", EventLog());

        // The engine id was released, so the same code can be created again from scratch.
        await Ok("/atlasfx create-persistent adminpurge1");
    }

    [AtlasScenario]
    public async Task Purge_Should_EvacuatePlayerToOverworld_When_StandingInside()
    {
        // A dedicated dimension, not one shared with other classes (e.g. flat): purge destroys it.
        await Ok("/atlasfx create-persistent adminpurge2");
        int dimId = await DimensionId("adminpurge2");

        ITestPlayer player = await JoinSurvivalPlayer("adminevacuee");
        await Ok("/atlasfx teleport-player adminevacuee adminpurge2");
        await LandedAt(player, dimId, 512, 512);

        CommandResult purged = await Ok("/manifold purge atlasfixture:adminpurge2");
        Assert.Contains("evacuated 1 player(s)", purged.Message);
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
    }

    [AtlasScenario]
    public async Task Relight_Should_BeRefused_When_CalledFromConsole()
    {
        CommandResult result = await World.ExecuteCommand("/manifold relight");
        Assert.False(result.Ok, "The console was allowed to relight.");
        Assert.Contains("player", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [AtlasScenario]
    public async Task Relight_Should_SucceedAndNameDimension_When_PlayerHoldsControlserver()
    {
        ITestPlayer player = await JoinSurvivalPlayer("adminrelighter");

        // Grant explicitly rather than assume the join-time default, then check it actually took.
        await Ok("/player adminrelighter role admin");
        Assert.True(player.Player.HasPrivilege("controlserver"), "test setup: player should be privileged here");

        await Ok("/atlasfx create-persistent adminrelightdim");
        int dimId = await DimensionId("adminrelightdim");
        await Ok("/atlasfx teleport-player adminrelighter adminrelightdim");
        await LandedAt(player, dimId, 512, 512);

        CommandResult relight = await player.ExecuteCommand("/manifold relight");
        Assert.True(relight.Ok, relight.Message);
        Assert.Contains($"dim {dimId}", relight.Message);
    }

    [AtlasScenario]
    public async Task AdminCommands_Should_BeRefused_When_PlayerLacksControlserver()
    {
        ITestPlayer player = await JoinSurvivalPlayer("adminplain");
        World.Api.Permissions.DenyPrivilege(player.Player.PlayerUID, "controlserver");
        Assert.False(player.Player.HasPrivilege("controlserver"), "test setup: player should not be privileged here");

        CommandResult purge = await player.ExecuteCommand("/manifold purge atlasfixture:flat");
        Assert.False(purge.Ok, "An unprivileged player was allowed to purge.");

        CommandResult relight = await player.ExecuteCommand("/manifold relight");
        Assert.False(relight.Ok, "An unprivileged player was allowed to relight.");
    }
}
