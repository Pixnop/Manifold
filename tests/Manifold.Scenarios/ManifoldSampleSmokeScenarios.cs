namespace Manifold.Scenarios;

using System.Globalization;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

/// <summary>
/// Smoke pass over every sample-mod command other than the mining dimension's reset lifecycle
/// (covered in more depth by <see cref="ManifoldSampleMiningScenarios"/>): each succeeds for a
/// player with the right privilege and lands or acts where samples/ManifoldSample/README.md
/// says. These boot-time dimensions (void/flat/dark/stream/vault) are static and shared across
/// the class host, so every scenario below uses its own player name, the same pattern
/// AdminCommandScenarios and PlayerTransitScenarios use for a shared host.
/// </summary>
/// <remarks>
/// The void portal block is not covered here: PortalBlockBase.OnEntityCollide only fires from
/// the engine's physics-step collision resolution, which needs the entity to actually move
/// (accumulate velocity and cross into the block); Atlas's ITestPlayer exposes only
/// <c>TeleportTo</c> (a direct position set, no velocity). A test player placed and repeatedly
/// re-teleported onto a placed <c>manifoldsample:voidportal</c> block never triggered the
/// collision within a generous tick budget, confirming this rather than assuming it.
/// </remarks>
[Trait("Category", "E2E")]
public class ManifoldSampleSmokeScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Sample_Should_BeLoaded_When_ServerBoots()
    {
        Assert.True(World.Api.ModLoader.IsModEnabled("manifoldsample"), "manifoldsample is not enabled in the embedded server.");
        Assert.NotNull(World.Api.ModLoader.GetModSystem("ManifoldSample.ManifoldSampleModSystem"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task VoidDim_Should_LandAtItsFixedSpawn_When_Entered()
    {
        int voidId = await SampleDimensionId("void");
        ITestPlayer player = await JoinSurvivalPlayer("samplevoid");

        CommandResult result = await player.ExecuteCommand("/voiddim");
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Teleported to manifoldsample:void.", result.Message);

        await LandedAt(player, voidId, 1024, 1024);
    }

    [AtlasScenario]
    public async Task FlatDim_Should_Teleport_When_Entered()
    {
        int flatId = await SampleDimensionId("flat");
        ITestPlayer player = await JoinSurvivalPlayer("sampleflat");

        CommandResult result = await player.ExecuteCommand("/flatdim");
        Assert.True(result.Ok, result.Message);
        await World.Until(() => player.Position.dimension == flatId, timeoutTicks: 600);
    }

    [AtlasScenario]
    public async Task DarkDim_Should_SealCeilingOverGeneratedColumns_When_Entered()
    {
        int darkId = await SampleDimensionId("dark");
        ITestPlayer player = await JoinSurvivalPlayer("sampledark");

        CommandResult result = await player.ExecuteCommand("/darkdim");
        Assert.True(result.Ok, result.Message);
        await World.Until(() => player.Position.dimension == darkId, timeoutTicks: 600);

        int px = (int)player.Position.X, pz = (int)player.Position.Z;
        var cap = new BlockPos(px, 12, pz, darkId);
        await BlockBecomes(cap, "game:rock-granite", timeoutTicks: 1200);

        var gap = new BlockPos(px, 6, pz, darkId);
        Assert.Equal("game:air", World.BlockAt(gap).Code.ToString());
    }

    [AtlasScenario]
    public async Task StreamDim_Should_GenerateAheadOfThePlayer_When_WalkingOut()
    {
        int streamId = await SampleDimensionId("stream");
        ITestPlayer player = await JoinSurvivalPlayer("samplestream");

        CommandResult result = await player.ExecuteCommand("/streamdim");
        Assert.True(result.Ok, result.Message);
        await World.Until(() => player.Position.dimension == streamId, timeoutTicks: 600);

        // Well outside the initial ensure-on-arrival area: only the streaming driver, not the
        // one-off transit generation, can be responsible for terrain showing up out here.
        var farLanding = new BlockPos((int)player.Position.X + 192, 8, (int)player.Position.Z, streamId);
        await player.TeleportTo(farLanding);

        var underFarLanding = new BlockPos(farLanding.X, 3, farLanding.Z, streamId);
        await BlockBecomes(underFarLanding, "game:rock-granite", timeoutTicks: 2400);
    }

    [AtlasScenario]
    public async Task VaultDim_Should_SwapInventoryOutAndBackIn_When_EnteredAndLeft()
    {
        ITestPlayer player = await JoinSurvivalPlayer("samplevault");
        await player.GiveItem("game:stick", 3);
        Assert.Equal(3, HotbarCount(player, "game:stick"));

        int vaultId = await SampleDimensionId("vault");
        CommandResult toVault = await player.ExecuteCommand("/vaultdim");
        Assert.True(toVault.Ok, toVault.Message);
        await World.Until(() => player.Position.dimension == vaultId, timeoutTicks: 600);
        Assert.Equal(0, HotbarCount(player, "game:stick"));

        CommandResult back = await player.ExecuteCommand("/overworlddim");
        Assert.True(back.Ok, back.Message);
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);
        Assert.Equal(3, HotbarCount(player, "game:stick"));
    }

    [AtlasScenario]
    public async Task SendTestItem_Should_ArriveInTheFlatDimension_When_Sent()
    {
        int flatId = await SampleDimensionId("flat");
        ITestPlayer player = await JoinSurvivalPlayer("samplesenditem");
        int px = (int)player.Position.X, pz = (int)player.Position.Z;

        CommandResult result = await player.ExecuteCommand("/sendtestitem");
        Assert.True(result.Ok, result.Message);
        Assert.Equal(
            "Sent a stick to the flat dimension; use /flatdim to find it near your X/Z.",
            result.Message);

        var area = new BlockPos(px, 6, pz, flatId).Area(16);
        await World.Until(
            () => World.EntitiesIn(area).Any(e =>
                e is EntityItem item && item.Itemstack?.Collectible?.Code?.ToString() == "game:stick"),
            timeoutTicks: 600);
    }

    // sendtestblock needs the caller to be looking at a block (IServerPlayer.CurrentBlockSelection),
    // populated in the real game by the client's aim/raycast; Atlas's ITestPlayer exposes no
    // aim/look-direction or raycast API to simulate that, only a direct teleport (see
    // Manifold.Scenarios/README.md), so only the "nothing targeted" guard is reachable through
    // a headless test player.
    [AtlasScenario]
    public async Task SendTestBlock_Should_RequireATargetedBlock_When_NothingIsAimedAt()
    {
        ITestPlayer player = await JoinSurvivalPlayer("samplesendblock");

        CommandResult result = await player.ExecuteCommand("/sendtestblock");
        Assert.False(result.Ok, "sendtestblock succeeded without the caller looking at a block.");
        Assert.Equal("Look at a block first, then run /sendtestblock.", result.Message);
    }

    [AtlasScenario]
    public async Task TempDim_Should_CreateThenForceDestroy_When_DrivenThroughCommands()
    {
        ITestPlayer player = await JoinSurvivalPlayer("sampletempdim");

        CommandResult created = await player.ExecuteCommand("/createtempdim");
        Assert.True(created.Ok, created.Message);

        int tempId = await SampleDimensionId("tempdim");
        await World.Until(() => player.Position.dimension == tempId, timeoutTicks: 600);

        CommandResult destroyed = await player.ExecuteCommand("/destroytempdim");
        Assert.True(destroyed.Ok, destroyed.Message);
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        CommandResult stateAfter = await World.ExecuteCommand("/atlasfx state manifoldsample:tempdim");
        Assert.False(stateAfter.Ok, "manifoldsample:tempdim is still registered after /destroytempdim.");
        Assert.Equal("unregistered", stateAfter.Message);
    }

    /// <summary>Reads a manifoldsample dimension's internal id from the fixture's state command.</summary>
    private async Task<int> SampleDimensionId(string path)
    {
        CommandResult state = await Ok("/atlasfx state manifoldsample:" + path);
        int separator = state.Message.IndexOf(':');
        return int.Parse(state.Message[(separator + 1)..], CultureInfo.InvariantCulture);
    }
}
