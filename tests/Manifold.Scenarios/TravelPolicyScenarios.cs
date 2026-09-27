namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Landing-position policy per <c>SpawnBehavior</c>, and the forced-game-mode transit hook, driven
/// through /atlasfx2 teleport-player-plain so no per-transit TransitionOptions override masks the
/// target dimension's own configured behavior.
/// </summary>
[Trait("Category", "E2E")]
public class TravelPolicyScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Anchor_Should_LandAtFixedSpawn_When_EnteringRegardlessOfSourcePosition()
    {
        int anchorId = await DimensionId("anchor");
        ITestPlayer player = await World.JoinPlayer("atlas_anchor");
        await player.TeleportTo(World.Spawn.Offset(70, 0, -55));

        await Ok("/atlasfx2 teleport-player-plain atlas_anchor anchor");

        await LandedAt(player, anchorId, 512, 512);
    }

    [AtlasScenario]
    public async Task Flat_Should_LandAtSourceCoordinates_When_NoSpawnBehaviorConfigured()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_flatcoords");
        BlockPos source = World.Spawn.Offset(35, 0, -20);
        await player.TeleportTo(source);

        await Ok("/atlasfx2 teleport-player-plain atlas_flatcoords flat");

        await LandedAt(player, flatId, source.X, source.Z);
    }

    [AtlasScenario]
    public async Task Memory_Should_FallBackToSourceCoordinates_ThenReturnToLastVisited_When_RevisitingAfterMoving()
    {
        int memoryId = await DimensionId("memory");
        ITestPlayer player = await World.JoinPlayer("atlas_memory");
        BlockPos firstEntry = World.Spawn.Offset(10, 0, 15);
        await player.TeleportTo(firstEntry);

        // First visit: LastVisited has nothing recorded yet and falls back to SameCoordinates.
        await Ok("/atlasfx2 teleport-player-plain atlas_memory memory");
        await LandedAt(player, memoryId, firstEntry.X, firstEntry.Z);

        // Move within the region already generated around the first landing, then leave: the
        // pre-transit position gets recorded for memory's dimension id on the way out.
        var revisitSpot = new BlockPos(player.Position.X + 5, player.Position.Y, player.Position.Z + 5, memoryId);
        await player.TeleportTo(revisitSpot);

        await Ok("/atlasfx2 teleport-player-plain atlas_memory overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        await Ok("/atlasfx2 teleport-player-plain atlas_memory memory");
        await LandedAt(player, memoryId, revisitSpot.X, revisitSpot.Z);
    }

    // Manifold bug (src/Manifold/Internal/TransitService.cs, TeleportPlayer): a ForcedGameMode is
    // applied on entry but nothing restores the player's PRIOR mode on a later transit into a
    // dimension without one, e.g. back to the overworld. Asserting the correct, documented
    // behavior (mode restored) so this scenario starts passing the moment the bug is fixed.
    [AtlasScenario]
    public async Task Creative_Should_ForceCreativeOnEntry_And_RestoreSurvival_When_LeavingToOverworld()
    {
        ITestPlayer player = await JoinSurvivalPlayer("atlas_creative");

        await Ok("/atlasfx2 teleport-player-plain atlas_creative creative");
        await World.Until(() => player.Player.WorldData.CurrentGameMode == EnumGameMode.Creative, timeoutTicks: 600);

        await Ok("/atlasfx2 teleport-player-plain atlas_creative overworld");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);
    }
}
