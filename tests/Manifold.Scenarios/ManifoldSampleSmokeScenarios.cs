namespace Manifold.Scenarios;

using System.Globalization;
using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
/// The void portal block and <c>/sendtestblock</c>'s success path both need real server-side
/// state <see cref="ITestPlayer"/> does not expose directly, but both are reachable through its
/// documented escape hatch, <see cref="ITestPlayer.Entity"/>, a live <c>EntityPlayer</c>:
/// <c>ServerPlayer.CurrentBlockSelection</c> is just <c>Entity.BlockSelection</c>, a public
/// field, so a scenario can set the aim directly instead of raycasting for it; and the engine's
/// own server-side collision resolution runs from <c>IRemotePhysics.OnReceivedClientPos</c>
/// (implemented by <c>EntityBehaviorPlayerPhysics</c>, reachable via
/// <c>Entity.SidedProperties.Behaviors</c>), the same handler a real client's position packet
/// drives, so moving the entity's position and replaying that call is a real collision, not a
/// simulated one.
/// </remarks>
[Trait("Category", "E2E")]
public class ManifoldSampleSmokeScenarios : ManifoldScenarioBase
{
    [AtlasScenario]
    public async Task Sample_Should_BeLoaded_When_ServerBoots()
    {
        Assert.True(World.Api.ModLoader.IsModEnabled("manifoldsample"), "manifoldsample is not enabled in the embedded server.");
        Assert.NotNull(World.Api.ModLoader.GetModSystem("ManifoldSample.ManifoldSampleModSystem"));
        Assert.NotNull(World.Api.World.GetBlock(new AssetLocation("manifoldsample", "voidportal")));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task VoidPortal_Should_TeleportPlayerToVoid_When_CollidedWith()
    {
        int voidId = await SampleDimensionId("void");
        ITestPlayer player = await JoinSurvivalPlayer("sampleportal");

        int px = (int)player.Position.X, pz = (int)player.Position.Z;
        var portalPos = new BlockPos(px + 4, 64, pz, 0);
        World.SetBlock("manifoldsample:voidportal", portalPos);
        await World.Ticks(2);

        // The engine's own server-side collision path (a real client position packet lands here
        // too), not a Manifold call: moving the entity across the portal's cell and replaying it
        // is the actual mechanism PortalBlockBase.OnEntityCollide relies on, driven directly
        // instead of through a simulated client that Atlas's ITestPlayer does not provide.
        var physics = player.Entity.SidedProperties.Behaviors.OfType<IRemotePhysics>().First();
        var above = new BlockPos(portalPos.X, portalPos.Y + 3, portalPos.Z, 0).ToVec3d().Add(0.5, 0, 0.5);
        var inside = new BlockPos(portalPos.X, portalPos.Y, portalPos.Z, 0).ToVec3d().Add(0.5, 0.5, 0.5);

        player.Entity.Pos.SetPos(above.X, above.Y, above.Z);
        physics.OnReceivedClientPos(1);
        await World.Ticks(1);

        bool teleported = false;
        for (int i = 0; i < 40 && !teleported; i++)
        {
            player.Entity.Pos.SetPos(inside.X, inside.Y, inside.Z);
            physics.OnReceivedClientPos(1);
            await World.Ticks(1);
            teleported = player.Position.dimension == voidId;
            if (!teleported)
            {
                player.Entity.Pos.SetPos(above.X, above.Y, above.Z);
                physics.OnReceivedClientPos(1);
                await World.Ticks(1);
                teleported = player.Position.dimension == voidId;
            }
        }

        Assert.True(teleported, "The player never transited through the void portal block.");
        await LandedAt(player, voidId, 1024, 1024);
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

    [AtlasScenario]
    public async Task SendTestBlock_Should_RequireATargetedBlock_When_NothingIsAimedAt()
    {
        ITestPlayer player = await JoinSurvivalPlayer("samplesendblock");

        CommandResult result = await player.ExecuteCommand("/sendtestblock");
        Assert.False(result.Ok, "sendtestblock succeeded without the caller looking at a block.");
        Assert.Equal("Look at a block first, then run /sendtestblock.", result.Message);
    }

    [AtlasScenario]
    public async Task SendTestBlock_Should_MoveChestWithContents_When_TargetedBlockIsSet()
    {
        int flatId = await SampleDimensionId("flat");
        ITestPlayer player = await JoinSurvivalPlayer("samplesendchest");
        int px = (int)player.Position.X, pz = (int)player.Position.Z;

        var chestPos = new BlockPos(px + 2, (int)player.Position.Y, pz, 0);
        await PlaceChest(chestPos, "stick", 4);

        // The client's aim/raycast populates IServerPlayer.CurrentBlockSelection, which is just
        // Entity.BlockSelection (a public field): set it directly through the documented
        // ITestPlayer.Entity escape hatch instead of simulating a raycast.
        player.Entity.BlockSelection = new BlockSelection
        {
            Position = chestPos,
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 0.5, 0.5),
        };

        CommandResult result = await player.ExecuteCommand("/sendtestblock");
        Assert.True(result.Ok, result.Message);
        Assert.Equal("game:air", World.BlockAt(chestPos).Code.ToString());

        var target = new BlockPos(px, 64, pz, flatId);
        await BlockBecomes(target, "game:chest-east", timeoutTicks: 600);
        AssertChestHolds(target, "stick", 4);
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

    [AtlasScenario]
    public async Task TempDim_Should_AutoReap_When_LastOccupantLeavesNormally()
    {
        ITestPlayer player = await JoinSurvivalPlayer("sampletempreap");

        CommandResult created = await player.ExecuteCommand("/createtempdim");
        Assert.True(created.Ok, created.Message);

        int tempId = await SampleDimensionId("tempdim");
        await World.Until(() => player.Position.dimension == tempId, timeoutTicks: 600);

        CommandResult left = await player.ExecuteCommand("/overworlddim");
        Assert.True(left.Ok, left.Message);
        await World.Until(() => player.Position.dimension == 0, timeoutTicks: 600);

        bool reaped = false;
        for (int i = 0; i < 200 && !reaped; i++)
        {
            CommandResult state = await World.ExecuteCommand("/atlasfx state manifoldsample:tempdim");
            reaped = !state.Ok && state.Message == "unregistered";
            if (!reaped)
            {
                await World.Ticks(1);
            }
        }

        Assert.True(reaped, "manifoldsample:tempdim is still registered after the last occupant left normally.");
    }

    /// <summary>Reads a manifoldsample dimension's internal id from the fixture's state command.</summary>
    private async Task<int> SampleDimensionId(string path)
    {
        CommandResult state = await Ok("/atlasfx state manifoldsample:" + path);
        int separator = state.Message.IndexOf(':');
        return int.Parse(state.Message[(separator + 1)..], CultureInfo.InvariantCulture);
    }
}
