namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// A dimension whose owning mod is not installed: seeded via the fixture's GameWorldSave hook
/// (InjectOrphanManifestEntry), which appends a Persistent manifest entry owned by "atlasghost"
/// on every save, requested by this class's staged ModConfig ({"SeedOrphan": true}). The next
/// boot's hydrate finds no such mod loaded and classifies the entry Quarantined instead of
/// Pending. RestartWorld (Atlas 0.7.0): each scenario here restarts the class host, which is
/// also what re-triggers the injection, so the orphan is guaranteed present after every restart
/// regardless of what an earlier scenario in the class did to it (purged or not) - the
/// order-independence Atlas requires within a class.
/// </summary>
[AtlasDataFiles("fixtures/quarantine", TargetPath = "ModConfig")]
[Trait("Category", "E2E")]
public class QuarantineScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Orphan_Should_BeQuarantined_When_ServerRestarts()
    {
        CommandResult state = await Ok("/atlasfx state atlasghost:orphan");
        Assert.Equal("Quarantined:900", state.Message);
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task Orphan_Should_RefuseTransit_When_PlayerAttemptsEntry()
    {
        // Join AFTER the restart: RestartWorld refuses joined players only when they were
        // already joined before it, so a player joined inside the scenario body is fine.
        ITestPlayer player = await World.JoinPlayer("orphanvisitor");
        await World.Ticks(2);

        CommandResult refused = await World.ExecuteCommand(
            "/atlasfx2 teleport-player-plain orphanvisitor atlasghost:orphan");
        Assert.False(refused.Ok, "Transit into a Quarantined dimension was accepted.");
        Assert.Contains("Quarantined", refused.Message);

        // Leave no joined player behind: this class shares its host across scenarios, and a
        // sibling scenario's RestartWorld would otherwise be refused by a still-joined player
        // from this one (Atlas does not guarantee scenario order, so any scenario here may run
        // next).
        await World.ExecuteCommand("/atlasfx2 kick orphanvisitor");
        await World.Until(() => !player.IsConnected, timeoutTicks: 200);
    }

    [AtlasScenario(RestartWorld = true)]
    public async Task Purge_Should_ReleaseOrphan_When_ManifoldPurgeIsUsed()
    {
        await Ok("/manifold purge atlasghost:orphan");

        CommandResult released = await World.ExecuteCommand("/atlasfx state atlasghost:orphan");
        Assert.False(released.Ok, "The orphan is still registered after purge.");
        Assert.Equal("unregistered", released.Message);
    }
}
