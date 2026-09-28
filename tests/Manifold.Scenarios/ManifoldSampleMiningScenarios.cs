namespace Manifold.Scenarios;

using System.Globalization;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Exercises the sample consumer mod's resettable mining dimension
/// (<c>ManifoldSampleModSystem.HandleMiningDim</c>/<c>HandleMiningReset</c>) through the real
/// <c>/miningdim</c> and <c>/miningreset</c> chat commands: creation lands a joined player in a
/// lit, breathable spawn room carved out of solid rock and ore (not inside rock), a second
/// <c>/miningdim</c> reuses the same dimension instead of recreating it, and
/// <c>/miningreset</c> evacuates any occupant, tears the dimension down, and leaves the next
/// <c>/miningdim</c> to regenerate it with a different ore layout. This is the one command pair
/// in the sample the manual smoke checklist calls out as needing the most hand-verification
/// (see samples/ManifoldSample/README.md), so it gets its own class rather than sharing the
/// smoke pass over the sample's simpler commands.
/// </summary>
/// <remarks>
/// <c>manifoldsample:mining</c> is a single global dimension code (unlike the fixture's
/// parameterized <c>/atlasfx create-ephemeral &lt;path&gt;</c>), and Atlas does not guarantee
/// scenario order within a class, so the full create/reuse/reset/recreate/reset-again narrative
/// lives in one scenario rather than being split across several that would silently depend on
/// running in a particular order. The privilege refusal below also touches the shared
/// dimension (it needs a real occupant to prove the reset was actually refused, not just that
/// the call returned an error), so it restores manifoldsample:mining to unregistered itself
/// before returning, keeping it order-independent alongside the lifecycle scenario.
/// </remarks>
[Trait("Category", "E2E")]
public class ManifoldSampleMiningScenarios : ManifoldScenarioBase
{
    private const string MiningPath = "manifoldsample:mining";
    private const int SpawnX = 16;
    private const int SpawnY = 22;
    private const int SpawnZ = 16;
    private const int ScanY = 20; // Below the carved room (floor..ceiling is 22..26): plain rock/ore fill.

    private static readonly string[] OreCodes =
    {
        "game:ore-medium-nativecopper-granite",
        "game:ore-medium-hematite-granite",
        "game:ore-quartz-granite",
    };

    [AtlasScenario]
    public async Task MiningDim_Should_CoverFullResetLifecycle_When_DrivenThroughRealCommands()
    {
        ITestPlayer player = await JoinSurvivalPlayer("minerlifecycle");
        await Ok("/player minerlifecycle role admin");
        Assert.True(player.Player.HasPrivilege("controlserver"), "test setup: player should be privileged here");

        // 1. First /miningdim creates the dimension and lands the player in the spawn room.
        CommandResult created = await player.ExecuteCommand("/miningdim");
        Assert.True(created.Ok, created.Message);
        Assert.Equal("Created manifoldsample:mining and sent you in.", created.Message);

        int firstId = await MiningDimensionId();
        await LandedAt(player, firstId, SpawnX, SpawnZ);
        await AssertLitBreathableRoom(firstId);
        string[] firstLayout = await ScanOreLayout(firstId);

        // 2. A second /miningdim reuses the dimension instead of recreating it.
        CommandResult reused = await player.ExecuteCommand("/miningdim");
        Assert.True(reused.Ok, reused.Message);
        Assert.Equal("Sent you to manifoldsample:mining.", reused.Message);
        Assert.Equal(firstId, await MiningDimensionId());
        await LandedAt(player, firstId, SpawnX, SpawnZ);

        // 3. /miningreset (admin) evacuates the occupant to the overworld and removes the dimension.
        CommandResult reset = await player.ExecuteCommand("/miningreset");
        Assert.True(reset.Ok, reset.Message);
        Assert.Equal("Reset manifoldsample:mining; run /miningdim to generate a fresh one.", reset.Message);
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        CommandResult stateAfterReset = await World.ExecuteCommand("/atlasfx state " + MiningPath);
        Assert.False(stateAfterReset.Ok, "The registry still knows manifoldsample:mining after a reset.");
        Assert.Equal("unregistered", stateAfterReset.Message);

        // 4. /miningreset again, with nothing registered, reports that instead of erroring.
        CommandResult resetAgain = await player.ExecuteCommand("/miningreset");
        Assert.True(resetAgain.Ok, resetAgain.Message);
        Assert.Equal("Nothing to reset - manifoldsample:mining is not currently registered.", resetAgain.Message);

        // 5. The next /miningdim recreates the dimension with a different ore layout (same shell/room).
        CommandResult recreated = await player.ExecuteCommand("/miningdim");
        Assert.True(recreated.Ok, recreated.Message);
        Assert.Equal("Created manifoldsample:mining and sent you in.", recreated.Message);

        // The id allocator may recycle the freed id (see RecyclingScenarios) or hand out a new
        // one; either is a valid fresh incarnation, so only the ore layout is compared below.
        int secondId = await MiningDimensionId();
        await LandedAt(player, secondId, SpawnX, SpawnZ);
        await AssertLitBreathableRoom(secondId);
        string[] secondLayout = await ScanOreLayout(secondId);

        Assert.NotEqual(firstLayout, secondLayout);
    }

    [AtlasScenario]
    public async Task MiningReset_Should_BeRefused_When_PlayerLacksControlserver()
    {
        // A privileged player creates and stays inside the dimension, so a refusal that merely
        // dropped the privilege check (the handler would then succeed either way) is caught: the
        // dimension and its occupant must still be there afterwards, not just the Ok flag.
        ITestPlayer owner = await JoinSurvivalPlayer("minerowner");
        CommandResult created = await owner.ExecuteCommand("/miningdim");
        Assert.True(created.Ok, created.Message);
        int dimId = await MiningDimensionId();
        await LandedAt(owner, dimId, SpawnX, SpawnZ);

        ITestPlayer player = await JoinSurvivalPlayer("minerplain");
        World.Api.Permissions.DenyPrivilege(player.Player.PlayerUID, "controlserver");
        Assert.False(player.Player.HasPrivilege("controlserver"), "test setup: player should not be privileged here");

        CommandResult result = await player.ExecuteCommand("/miningreset");
        Assert.False(result.Ok, "An unprivileged player was allowed to reset the mining dimension.");

        CommandResult state = await World.ExecuteCommand("/atlasfx state " + MiningPath);
        Assert.True(state.Ok, "manifoldsample:mining was torn down by a refused /miningreset.");
        Assert.StartsWith("Active:", state.Message);
        Assert.Equal(dimId, owner.Position.dimension);

        // Restore the shared manifoldsample:mining code to unregistered: the lifecycle scenario
        // above assumes a fresh dimension on its first /miningdim, and Atlas does not guarantee
        // scenario order within a class.
        await owner.ExecuteCommand("/miningreset");
    }

    /// <summary>Reads manifoldsample:mining's internal id from the fixture's state command.</summary>
    private async Task<int> MiningDimensionId()
    {
        CommandResult state = await Ok("/atlasfx state " + MiningPath);
        int separator = state.Message.IndexOf(':');
        return int.Parse(state.Message[(separator + 1)..], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asserts the spawn room is breathable and lit, and that solid rock with at least one ore
    /// block sits nearby (dug into once the player leaves the carved room).
    /// </summary>
    private async Task AssertLitBreathableRoom(int dimId)
    {
        // Off-centre so this does not land on the light block's own cell (see the light check below).
        var airFeet = new BlockPos(SpawnX + 1, SpawnY, SpawnZ, dimId);
        var airHead = new BlockPos(SpawnX + 1, SpawnY + 1, SpawnZ, dimId);
        await World.Until(
            () => World.BlockAt(airFeet).Code.ToString() == "game:air"
                && World.BlockAt(airHead).Code.ToString() == "game:air",
            timeoutTicks: 1200);

        // The player's actual landing cell: feet on the light block's own position (non-solid,
        // walkable), head one block up. Neither is the off-centre probe above.
        var feetPos = new BlockPos(SpawnX, SpawnY, SpawnZ, dimId);
        Assert.Equal("game:torch-basic-lit-up", World.BlockAt(feetPos).Code.ToString());

        var headPos = new BlockPos(SpawnX, SpawnY + 1, SpawnZ, dimId);
        Assert.Equal("game:air", World.BlockAt(headPos).Code.ToString());

        // Generation readiness probe a few blocks out from the room, same pattern as the other
        // worldgen scenarios (DarkSkyScenarios, EphemeralDimensionScenarios): wait for the real
        // rock fill to land before scanning for it.
        var genProbe = new BlockPos(SpawnX + 8, ScanY, SpawnZ, dimId);
        await BlockBecomes(genProbe, "game:rock-granite", timeoutTicks: 1200);

        bool sawRock = false, sawOre = false;
        for (int dx = -8; dx <= 8 && !(sawRock && sawOre); dx++)
        {
            for (int dz = -8; dz <= 8 && !(sawRock && sawOre); dz++)
            {
                string code = World.BlockAt(new BlockPos(SpawnX + dx, ScanY, SpawnZ + dz, dimId)).Code.ToString();
                sawRock |= code == "game:rock-granite";
                sawOre |= Array.IndexOf(OreCodes, code) >= 0;
            }
        }

        Assert.True(sawRock, "No rock block found near the mining dim spawn.");
        Assert.True(sawOre, "No ore block found near the mining dim spawn.");
    }

    /// <summary>Block codes at a fixed grid of positions, used to prove a reset changes the ore layout.</summary>
    private Task<string[]> ScanOreLayout(int dimId)
    {
        var codes = new List<string>();
        for (int dx = -6; dx <= 6; dx += 3)
        {
            for (int dz = -6; dz <= 6; dz += 3)
            {
                codes.Add(World.BlockAt(new BlockPos(SpawnX + dx, ScanY, SpawnZ + dz, dimId)).Code.ToString());
            }
        }

        return Task.FromResult(codes.ToArray());
    }
}
