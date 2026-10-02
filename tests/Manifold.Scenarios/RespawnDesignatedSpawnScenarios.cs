namespace Manifold.Scenarios;

using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

/// <summary>
/// A respawn point that designates a dimension: the temporal gear stores the player's InternalY
/// (local Y plus 32768 times the dimension id), so a gear used inside a custom dimension gives the
/// game a spawn the game's own respawn does not decode. Split from <see cref="RespawnScenarios"/>
/// only because the test server caps how many players one world can hold.
/// </summary>
[Trait("Category", "E2E")]
public class RespawnDesignatedSpawnScenarios : ManifoldScenarioBase
{
    private const double Tolerance = 0.01;

    [AtlasScenario]
    public async Task Respawn_Should_LandInTheDimensionTheSpawnPointDesignates_When_AGearWasUsedInsideACustomDimension()
    {
        int flatId = await DimensionId("flat");
        int creativeId = await DimensionId("creative");
        ITestPlayer player = await Enter("rsp_gear", "flat", flatId);

        // A temporal gear stores the player's InternalY, which is the local Y plus 32768 times the
        // dimension id: a spawn set inside the creative dimension.
        player.Player.SetSpawnPosition(new PlayerSpawnPos(520, (creativeId * 32768) + 6, 520));

        await DieAndRespawn(player, creativeId);

        AssertAt(player, 520.5, 6, 520.5);
        await World.Until(() => player.Player.WorldData.CurrentGameMode == EnumGameMode.Creative, timeoutTicks: 200);
    }

    [AtlasScenario]
    public async Task Respawn_Should_LandInTheDesignatedDimension_When_ThePlayerDiedInTheOverworldWithAGearSetInACustomDimension()
    {
        int creativeId = await DimensionId("creative");
        ITestPlayer player = await JoinSurvivalPlayer("rsp_ovgear");
        player.Player.SetSpawnPosition(new PlayerSpawnPos(520, (creativeId * 32768) + 6, 520));
        ClearEventLog();

        // The game's own respawn would leave the player at a Y above the whole world, in the overworld.
        await DieAndRespawn(player, creativeId);

        AssertAt(player, 520.5, 6, 520.5);
        await World.Until(() => player.Player.WorldData.CurrentGameMode == EnumGameMode.Creative, timeoutTicks: 200);
        Assert.Equal(["left:overworld->creative", "entered:creative", "respawn:creative"], EventLog());
    }

    [AtlasScenario]
    public async Task Respawn_Should_LandAtTheWorldSpawn_When_TheDimensionTheSpawnPointDesignatesIsEphemeral()
    {
        await Ok("/atlasfx create-ephemeral rsptemp");
        int tempId = await DimensionId("rsptemp");
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_eph", "flat", flatId);
        player.Player.SetSpawnPosition(new PlayerSpawnPos(520, (tempId * 32768) + 6, 520));
        EntityPos world = World.Api.World.DefaultSpawnPosition;

        // An ephemeral dimension's id is recycled, so a spawn set in one is not trusted.
        await DieAndRespawn(player, 0);

        AssertAt(player, world.X, world.Y, world.Z);
    }

    [AtlasScenario]
    public async Task Respawn_Should_LandAtTheWorldSpawn_When_TheDimensionTheSpawnPointDesignatesIsNotActive()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await Enter("rsp_ghost", "flat", flatId);
        const int unusedDimension = 700;
        player.Player.SetSpawnPosition(new PlayerSpawnPos(520, (unusedDimension * 32768) + 6, 520));
        EntityPos world = World.Api.World.DefaultSpawnPosition;

        await DieAndRespawn(player, 0);

        AssertAt(player, world.X, world.Y, world.Z);
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
