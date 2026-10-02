namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// A player who dies inside a custom dimension and presses "Respawn" (issue #134). The engine's
/// respawn handler teleports with X/Y/Z only, so without Manifold the player comes back alive in
/// the dimension they died in, at the overworld spawn's coordinates. The scenarios drive a real
/// death (<c>Entity.Die</c>) and the engine's own respawn event, then read where the player ends up.
/// </summary>
[Trait("Category", "E2E")]
public class RespawnScenarios : ManifoldScenarioBase
{
    private const double Tolerance = 0.01;

    [AtlasScenario]
    public async Task Respawn_Should_LandInOverworldWithinSpawnRadius_When_PlayerDiesInCustomDimension()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_plain", "flat", flatId);
        FuzzyEntityPos spawn = player.Player.GetSpawnPosition(consumeSpawnUse: false);

        await DieAndRespawn(player, 0);

        // No bed and no gear: the engine drops the player at a random spot within the world's spawn
        // radius around the default spawn, so the position is only known up to that radius.
        EntityPos pos = player.Entity.Pos;
        Assert.True(spawn.Radius > 0f, "the test world has no spawn radius, so the random spot cannot be checked");
        Assert.InRange(Math.Abs(pos.X - spawn.X), 0, spawn.Radius + 1);
        Assert.InRange(Math.Abs(pos.Z - spawn.Z), 0, spawn.Radius + 1);
    }

    [AtlasScenario]
    public async Task Respawn_Should_LandExactlyAtTheGearSpawn_When_PlayerWithACustomSpawnDiesInCustomDimension()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_bed");
        player.Player.SetSpawnPosition(new PlayerSpawnPos(512100, 40, 512200));
        await Ok("/atlasfx2 teleport-player-plain rsp_bed flat");
        await LandedAt(player, flatId, 512, 512);
        FuzzyEntityPos spawn = player.Player.GetSpawnPosition(consumeSpawnUse: false);
        Assert.InRange(spawn.Radius, 0f, 0f);

        await DieAndRespawn(player, 0);

        AssertAt(player, spawn.X, spawn.Y, spawn.Z);
    }

    [AtlasScenario]
    public async Task Respawn_Should_HandBackTheGameMode_When_TheDimensionForcedOne()
    {
        int creativeId = await DimensionId("creative");
        ITestPlayer player = await Enter("rsp_mode", "creative", creativeId);
        await World.Until(() => player.Player.WorldData.CurrentGameMode == EnumGameMode.Creative, timeoutTicks: 200);

        await DieAndRespawn(player, 0);

        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);
    }

    [AtlasScenario]
    public async Task Respawn_Should_KeepTheGameMode_When_ACreativePlayerDiesInADimensionThatForcesNone()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_free");
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        player.Player.BroadcastPlayerData(true);
        await Ok("/atlasfx2 teleport-player-plain rsp_free flat");
        await LandedAt(player, flatId, 512, 512);

        await DieAndRespawn(player, 0);

        Assert.Equal(EnumGameMode.Creative, player.Player.WorldData.CurrentGameMode);
    }

    [AtlasScenario]
    public async Task Respawn_Should_RaiseLeftAndEnteredFlaggedAsRespawnButNotEnteringOrArriving_When_PlayerDiesInCustomDimension()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_events", "flat", flatId);
        ClearEventLog();

        await DieAndRespawn(player, 0);

        // A respawn cannot be refused, so the two cancellable events are not raised; the other two
        // are (flagged IsRespawn), so a mod that follows players across dimensions sees this one too.
        Assert.Equal(["left:flat->overworld", "entered:overworld", "respawn:overworld"], EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_NotRaiseAnyTransitEvent_When_PlayerDiesInTheOverworld()
    {
        ITestPlayer player = await JoinSurvivalPlayer("rsp_over");
        FuzzyEntityPos spawn = player.Player.GetSpawnPosition(consumeSpawnUse: false);
        ClearEventLog();

        await DieAndRespawn(player, 0);

        Assert.Empty(EventLog());
        Assert.InRange(Math.Abs(player.Entity.Pos.X - spawn.X), 0, spawn.Radius + 1);
        Assert.Equal(EnumGameMode.Survival, player.Player.WorldData.CurrentGameMode);
    }

    [AtlasScenario]
    public async Task Respawn_Should_NotMoveThePlayer_When_RespawnIsPressedWhileAlive()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_alive", "flat", flatId);
        ClearEventLog();

        // The engine ignores a respawn request from a living player but still raises its event for
        // mods: a client that sends one must not be able to leave the dimension that way.
        PressRespawn(player);
        await World.Ticks(20);

        Assert.Equal(flatId, player.Position.dimension);
        Assert.Empty(EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_RestoreOverworldItemsAndKeepNothingTwice_When_PlayerDiesInASeparateInventoryDimension()
    {
        int vaultId = await DimensionId("vault");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_vault");
        await player.GiveItem("game:stick", 5);
        await Ok("/atlasfx2 teleport-player-plain rsp_vault vault");
        await LandedAt(player, vaultId, 512, 512);
        await World.Until(() => HotbarCount(player, "game:stick") == 0, timeoutTicks: 200);
        await player.GiveItem("game:flint", 3);
        Vec3d fell = player.Entity.Pos.XYZ;

        await DieAndRespawn(player, 0);

        // The overworld set is back as it was; the flint died with the player (the game dropped it
        // where they fell, in the vault) and is not carried over, nor kept in the vault's own set.
        await World.Until(() => HotbarCount(player, "game:stick") == 5, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:flint"));

        // Nothing is lost: the dropped flint lies where the player fell, in the vault, and the
        // overworld sticks were not dropped there.
        List<ItemStack> ground = ItemsOnTheGround(fell);
        Assert.Equal(3, CountOf(ground, "game:flint"));
        Assert.Equal(0, CountOf(ground, "game:stick"));

        await Ok("/atlasfx2 teleport-player-plain rsp_vault vault");
        await LandedAt(player, vaultId, 512, 512);
        await World.Until(() => HotbarCount(player, "game:stick") == 0, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:flint"));
    }

    [AtlasScenario]
    public async Task Respawn_Should_KeepTheVaultItemsAside_When_TheGameKeepsInventoryOnDeath()
    {
        int vaultId = await DimensionId("vault");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_keep");
        await player.GiveItem("game:stick", 5);
        await Ok("/atlasfx2 teleport-player-plain rsp_keep vault");
        await LandedAt(player, vaultId, 512, 512);
        await World.Until(() => HotbarCount(player, "game:stick") == 0, timeoutTicks: 200);
        await player.GiveItem("game:flint", 3);
        Vec3d fell = player.Entity.Pos.XYZ;

        // What the world's "keep inventory" death penalty does to the player entity type.
        EntitySidedProperties server = player.Entity.Properties.Server;
        ITreeAttribute? original = server.Attributes;
        server.Attributes = new TreeAttribute();
        server.Attributes.SetBool("keepContents", true);
        try
        {
            await DieAndRespawn(player, 0);
        }
        finally
        {
            server.Attributes = original;
        }

        await World.Until(() => HotbarCount(player, "game:stick") == 5, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:flint"));
        Assert.Equal(0, CountOf(ItemsOnTheGround(fell), "game:flint")); // kept, not dropped

        await Ok("/atlasfx2 teleport-player-plain rsp_keep vault");
        await LandedAt(player, vaultId, 512, 512);
        await World.Until(() => HotbarCount(player, "game:flint") == 3, timeoutTicks: 200);
        Assert.Equal(0, HotbarCount(player, "game:stick"));
    }

    [AtlasScenario]
    public async Task Respawn_Should_StayInTheDimensionAtItsFixedSpawn_When_TheDimensionKeepsItsDead()
    {
        int bunkerId = await DimensionId("bunker");
        ITestPlayer player = await Enter("rsp_bunker", "bunker", bunkerId);
        await player.TeleportTo(new BlockPos(530, 6, 530, bunkerId));
        ClearEventLog();

        await DieAndRespawn(player, bunkerId);

        // The fixed spawn is (512, 8, 512); the player was far from it when they died.
        AssertAt(player, 512.5, 8, 512.5);
        Assert.Empty(EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_FallBackToTheOverworld_When_TheDimensionKeepsItsDeadButHasNoSpawnPoint()
    {
        int nospawnId = await DimensionId("nospawn");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_nosp");
        await World.ExecuteCommand("/atlasfx teleport-player rsp_nosp nospawn");
        await World.Until(() => player.Position.dimension == nospawnId, timeoutTicks: 600);

        await DieAndRespawn(player, 0);

        Assert.True(player.Entity.Alive);
    }

    [AtlasScenario]
    public async Task Respawn_Should_NotMoveThePlayer_When_TheyWereRevivedInPlaceAndThenSendARespawnRequest()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_revive", "flat", flatId);
        Kill(player);
        await World.Until(() => !player.Entity.Alive, timeoutTicks: 200);
        await World.Ticks(5);

        // What another player's healing item does: revive in place, with no respawn request.
        player.Entity.Revive();
        await World.Ticks(5);
        Vec3d standing = player.Entity.Pos.XYZ;
        ClearEventLog();

        // A modified client can send a respawn request whenever it likes; the game ignores it for a
        // living player but still raises its event.
        PressRespawn(player);
        await World.Ticks(20);

        Assert.True(player.Entity.Alive);
        Assert.Equal(flatId, player.Position.dimension);
        Assert.Equal(standing, player.Entity.Pos.XYZ);
        Assert.Empty(EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_MoveThePlayerOut_When_TheyDisconnectedWhileDeadAndCameBack()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_rejoin", "flat", flatId);
        Kill(player);
        await World.Until(() => !player.Entity.Alive, timeoutTicks: 200);

        await World.ExecuteCommand("/atlasfx2 kick rsp_rejoin");
        await World.Until(() => !player.IsConnected, timeoutTicks: 200);
        ITestPlayer rejoined = await World.JoinPlayer("rsp_rejoin");
        await World.Until(() => rejoined.Position.dimension == flatId, timeoutTicks: 600);
        Assert.False(rejoined.Entity.Alive, "the player was expected to come back still dead");

        PressRespawn(rejoined);
        await AliveIn(rejoined, 0);
    }

    [AtlasScenario]
    public async Task Respawn_Should_LeaveThePlayerWhereTheEngineWouldPutThem_When_TheDimensionWasRemovedWhileTheyWereDead()
    {
        await Ok("/atlasfx create-ephemeral rspghost");
        int ghostId = await DimensionId("rspghost");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_gone");
        await World.ExecuteCommand("/atlasfx teleport-player rsp_gone rspghost");
        await World.Until(() => player.Position.dimension == ghostId, timeoutTicks: 600);
        Kill(player);
        await World.Until(() => !player.Entity.Alive, timeoutTicks: 200);

        // Forcing the removal evacuates the (dead) occupant to the overworld first.
        await Ok("/atlasfx2 force-remove rspghost");
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        ClearEventLog();

        PressRespawn(player);
        await AliveIn(player, 0);
        Assert.Empty(EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_LeaveThePlayerWhereTheEngineWouldPutThem_When_TheJoinRescueAlreadyBroughtThemOutOfTheRemovedDimension()
    {
        await Ok("/atlasfx create-ephemeral rspjoin");
        int joinId = await DimensionId("rspjoin");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_join");
        await World.ExecuteCommand("/atlasfx teleport-player rsp_join rspjoin");
        await World.Until(() => player.Position.dimension == joinId, timeoutTicks: 600);
        Kill(player);
        await World.Until(() => !player.Entity.Alive, timeoutTicks: 200);
        await World.ExecuteCommand("/atlasfx2 kick rsp_join");
        await World.Until(() => !player.IsConnected, timeoutTicks: 200);
        await Ok("/atlasfx remove rspjoin");

        // The dimension is gone: Manifold's rescue on join takes the (still dead) player to the
        // overworld. Their respawn is then an ordinary overworld one.
        ITestPlayer rejoined = await World.JoinPlayer("rsp_join");
        await World.Until(() => rejoined.Position.dimension == 0, timeoutTicks: 600);
        Assert.False(rejoined.Entity.Alive, "the player was expected to come back still dead");
        ClearEventLog();

        PressRespawn(rejoined);
        await AliveIn(rejoined, 0);
        Assert.Empty(EventLog());
    }

    private async Task<ITestPlayer> Enter(string name, string dimension, int dimensionId)
    {
        ITestPlayer player = await JoinSurvivalPlayer(name);
        await Ok($"/atlasfx2 teleport-player-plain {name} {dimension}");
        await LandedAt(player, dimensionId, 512, 512);
        return player;
    }

    private async Task DieAndRespawn(ITestPlayer player, int expectedDimension)
    {
        Kill(player);
        await World.Until(() => !player.Entity.Alive, timeoutTicks: 200);

        // The death is announced on the next server tick: wait for it, as a real client's respawn
        // request can only follow it.
        await World.Ticks(5);
        PressRespawn(player);
        await AliveIn(player, expectedDimension);
    }

    private static void AssertAt(ITestPlayer player, double x, double y, double z)
    {
        EntityPos pos = player.Entity.Pos;
        Assert.InRange(Math.Abs(pos.X - x), 0, Tolerance);
        Assert.InRange(Math.Abs(pos.Y - y), 0, Tolerance);
        Assert.InRange(Math.Abs(pos.Z - z), 0, Tolerance);
    }
}
