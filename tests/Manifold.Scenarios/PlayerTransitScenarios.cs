namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

// rollback-stage2-candidate: joined test players hard-refuse stage 1 rollback with an
// AtlasSetupException (player entity state is not captured). A stage 2 rollback would also give
// the event-flag assertions real isolation: the atlasfixture:event:* SaveGame flags accumulate
// across scenarios on a shared host, so today only the first scenario asserting a given flag is
// meaningful. Needs: player position/inventory/stats capture plus the existing SaveGame restore.
[Trait("Category", "E2E")]
public class PlayerTransitScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Player_Should_RoundTripAcrossDimensions_When_Teleported()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlastester");
        await World.Ticks(2);

        CommandResult toFlat = await World.ExecuteCommand("/atlasfx teleport-player atlastester flat");
        Assert.True(toFlat.Ok, "teleport-player reported failure.");
        await World.Until(() => player.Position.dimension == flatId, timeoutTicks: 600);
        Assert.True(player.Stats.Health > 0, "Player died during transit.");
        Assert.True(FlagIsSet("atlasfixture:event:player-entered:flat"), "PlayerEntered event did not fire for the target dimension.");
        Assert.True(FlagIsSet("atlasfixture:event:player-left:overworld"), "PlayerLeft event did not fire for the source dimension.");

        CommandResult toOverworld = await World.ExecuteCommand("/atlasfx teleport-player atlastester overworld");
        Assert.True(toOverworld.Ok, "teleport-player reported failure.");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.True(FlagIsSet("atlasfixture:event:player-left:flat"), "PlayerLeft event did not fire on the way back.");
    }

    [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
    public async Task Player_Should_BeDismounted_And_LeaveTheMountBehind_When_Teleported()
    {
        // A distinct player name: this class's host is shared across its scenarios, and
        // Player_Should_RoundTripAcrossDimensions_When_Teleported already joins "atlastester".
        const string playerName = "atlasrider";

        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer(playerName);
        await World.Ticks(2);

        BlockPos boatSpot = World.Spawn.Offset(3, 0, 3);
        Entity boat = World.SpawnEntity("game:boat-raft-aged", boatSpot);
        await World.Ticks(2);

        CommandResult mounted = await World.ExecuteCommand($"/atlasfx mount-player {playerName} {boat.EntityId}");
        Assert.True(mounted.Ok, mounted.Message);
        Assert.NotNull(player.Entity.MountedOn);

        CommandResult result = await World.ExecuteCommand($"/atlasfx teleport-player {playerName} flat");
        Assert.True(result.Ok, "teleport-player reported failure.");

        // Wait for the actual landing column (the "flat" dimension's fixed spawn at 512,512, same
        // as every other scenario landing there), not just the dimension flip: TryUnmount's own
        // DidUnmount runs a second, unrelated teleport of the player next to the mount's free-exit
        // spot near boatSpot, and this must not be mistaken for the transit having landed.
        await LandedAt(player, flatId, 512, 512);

        // The player was released from the seat before moving on ...
        Assert.Null(player.Entity.MountedOn);

        // ... and the boat itself never left the overworld.
        Assert.Equal(0, boat.Pos.Dimension);
        Assert.Contains(World.EntitiesIn(boatSpot.Area(16)), e => e.EntityId == boat.EntityId);
    }
}
