namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Xunit;

/// <summary>
/// An origin recorded before a server restart is still there after it. The staged fixture
/// (fixtures/origin) writes, on the first boot, an origin blob for the test player
/// <c>atlas-originkept</c> (who came from "flat", at an exact fractional spot, facing a known yaw);
/// the restarted server loads it, and this scenario then uses it for real through the public API.
/// The blob is seeded raw because Atlas cannot restart a class that has joined players, so no player
/// can transit before the restart; the write side of the round trip (a real transit, then a real
/// world save, then reading the key back) is covered by
/// <see cref="OriginScenarios.Origin_Should_BeWrittenToTheSaveGame_When_APlayerTransitsAndTheWorldSaves"/>.
/// A separate class with its own fixture data directory, for the same reason the schema restart
/// scenarios are: Atlas does not guarantee scenario order within a class.
/// </summary>
[AtlasDataFiles("fixtures/origin", TargetPath = "ModConfig")]
[Trait("Category", "E2E")]
public class OriginPersistenceScenarios : ManifoldScenarioBase
{
    [AtlasScenario(RestartWorld = true)]
    public async Task Origin_Should_StillBeUsable_When_TheServerRestarted()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("originkept");
        await World.Ticks(2);
        Assert.Equal("atlas-originkept", player.Player.PlayerUID); // the uid the fixture seeded the blob under

        // Loaded from the save: the dimension it names resolved (same id, same code) and the exact
        // doubles and yaw came back unrounded.
        Assert.Equal("atlasfixture:flat|515.25|6|509.75|1.25", (await Ok("/atlasfx3 origin originkept")).Message);

        Assert.Equal("returned", (await Ok("/atlasfx3 return originkept")).Message);
        await World.Until(
            () => player.Entity.Pos.Dimension == flatId
                && Math.Abs(player.Entity.Pos.X - 515.25) < 1e-4
                && Math.Abs(player.Entity.Pos.Y - 6.0) < 1e-4
                && Math.Abs(player.Entity.Pos.Z - 509.75) < 1e-4
                && Math.Abs(player.Entity.Pos.Yaw - 1.25) < 1e-4,
            timeoutTicks: 600);
    }
}
