namespace Manifold.Scenarios.Compat;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Loads fixtures/upgrade-from-0.5.1.vcdbs, a world played entirely with the published Manifold
/// 0.5.1 release (see tests/Manifold.Scenarios.CompatFixtures/UpgradeFixtureBuilderScenarios),
/// with THIS repo's dev build: the "upgrade" direction. The assembly-default mod set already
/// stages the dev Manifold and the compat fixture (see the csproj), so no [AtlasWorld] override
/// is needed beyond the fixture itself.
/// </summary>
[Trait("Category", "E2E")]
[AtlasWorld(SaveFile = "fixtures/upgrade-from-0.5.1.vcdbs")]
public class UpgradeVerifyScenarios : CompatVerifyScenarioBase
{
    [AtlasScenario]
    public async Task DevBuild_Should_KeepDimensionIdTerrainPositionsInventoryAndForcedMode_When_OpeningA051World()
    {
        // Same persistent dimension, same internal id: the manifest round-tripped across the
        // version change, not just across an ordinary restart.
        int dimId = await CompatDimensionId();
        Assert.Equal(PreviousDimensionId(), dimId);

        // Reconnect the SAME player (Atlas derives a stable id from the player name), which
        // restores them exactly where the 0.5.1 side left them: inside "compat", forced
        // Creative, holding the item their separate-inventory profile gave them there. This also
        // loads the chunk they are standing in, a precondition for the block reads below: a
        // fresh boot loads chunks on demand, not eagerly for every previously-generated column.
        ITestPlayer player = await World.JoinPlayer("compat051");
        await World.Until(() => player.Position.dimension == dimId, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);
        Assert.True(HotbarCount(player, "game:gear-rusty") >= 3, "The separate-inventory item did not survive the version change.");

        // The block a player placed: still there, not overwritten by a regeneration.
        BlockPos placedAt = ReadPosition("placed", dimId);
        await World.Until(() => World.BlockAt(placedAt).Code?.ToString() != "game:air", timeoutTicks: 200);
        Assert.Equal("game:chest-east", World.BlockAt(placedAt).Code?.ToString());

        // Generated terrain intact around it: the slab worldgen's granite, one block down, in
        // the same column the 0.5.1-side player actually stood in (so it is guaranteed to have
        // been generated, unlike an arbitrary far-away column).
        var slabBelow = new BlockPos(placedAt.X, 2, placedAt.Z, dimId);
        Assert.Equal("game:rock-granite", World.BlockAt(slabBelow).Code?.ToString());

        // Saved pre-forced game mode: leaving restores Survival, the mode the 0.5.1 side saved
        // right before the world was harvested.
        await Ok("/manicompat leave compat051");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);

        // Last-visited position: moving somewhere else first, THEN re-entering, still lands back
        // at the coordinates the 0.5.1 side recorded (not the current position, and not the
        // dimension's own spawn): proof the memory is read from the persisted profile, not from
        // whatever happens to be the player's position right now.
        BlockPos lastVisited = ReadPosition("lastvisited", dimId);
        await player.TeleportTo(World.Spawn.Offset(500, 0, 500));
        await Ok("/manicompat enter compat051");
        await LandedAt(player, dimId, lastVisited.X, lastVisited.Z);
    }
}
