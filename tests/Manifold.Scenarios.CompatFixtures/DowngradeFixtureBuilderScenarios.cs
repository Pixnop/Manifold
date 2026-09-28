namespace Manifold.Scenarios.CompatFixtures;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Builds the world `atlas fixture` harvests as fixtures/downgrade-from-dev.vcdbs (see
/// README.md): a world played with this repo's dev build (which also writes the
/// "manifold:schema" sidecar) and the same compat fixture, compiled against the 0.5.1 API but
/// staged here against the dev Manifold.dll (its frozen AssemblyVersion is exactly what makes
/// that combination load; see the fixture project's csproj comment).
/// Manifold.Scenarios.Compat's DowngradeVerifyScenarios then boots that exact save with the
/// published 0.5.1 release and asserts nothing broke, in particular that the sidecar the dev
/// build wrote (something 0.5.1 has never heard of) does not stop the world from loading or
/// regenerate terrain over the placed block.
/// </summary>
[Trait("Category", "E2E")]
public class DowngradeFixtureBuilderScenarios : CompatFixtureScenarioBase
{
    [AtlasScenario]
    public async Task BuildTheDowngradeFixture()
    {
        int dimId = await CompatDimensionId();

        ITestPlayer player = await World.JoinPlayer("compatdev");
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Player.BroadcastPlayerData(true);
        BlockPos start = World.Spawn.Offset(-40, 0, -40);
        await player.TeleportTo(start);

        await Ok("/manicompat enter compatdev");
        await LandedAt(player, dimId, start.X, start.Z);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);

        var placedAt = new BlockPos(player.Position.X, player.Position.Y + 1, player.Position.Z, dimId);
        World.SetBlock("game:chest-east", placedAt);
        await World.Ticks(2);
        PublishPosition("placed", placedAt);

        await player.GiveItem("game:gear-rusty", 3);

        var lastVisited = new BlockPos(player.Position.X + 3, player.Position.Y, player.Position.Z + 3, dimId);
        await player.TeleportTo(lastVisited);
        await Ok("/manicompat leave compatdev");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);
        PublishPosition("lastvisited", lastVisited);

        await Ok("/manicompat enter compatdev");
        await LandedAt(player, dimId, lastVisited.X, lastVisited.Z);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);

        // Whether the dev build actually wrote the "manifold:schema" sidecar this world is about
        // to carry into the 0.5.1 boot is asserted by DowngradeVerifyScenarios instead: the
        // sidecar is only written on a real GameWorldSave, which has not fired yet at this point
        // in the live session (it fires on the graceful shutdown `atlas fixture` triggers after
        // this scenario returns).
    }
}
