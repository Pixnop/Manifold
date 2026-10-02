namespace Manifold.Scenarios;

using System.Globalization;
using Atlas.Api;
using Atlas.XUnit;
using Manifold.Internal;
using Manifold.Internal.Networking;
using Vintagestory.API.Common;
using Xunit;

/// <summary>
/// Arrival yaw (<c>TransitionOptions.Yaw</c>) and return to origin (<c>GetOrigin</c> /
/// <c>TryReturnPlayer</c>) on a real server. Joined players mean this class cannot roll back, and
/// each scenario joins its own uniquely named player so the shared host never confuses them. The
/// persistence half lives in <see cref="OriginPersistenceScenarios"/>.
/// </summary>
// rollback-stage2-candidate: same blocker as PlayerTransitScenarios (joined test players hard-refuse
// stage 1 rollback). Origins recorded in the plugin store are in-memory and per player uid, so a
// stage 2 rollback would need to resync that store too (ManifoldModSystem.OnAtlasRollbackRestored
// already reloads it from the SaveGame blob).
[Trait("Category", "E2E")]
public class OriginScenarios : ManifoldScenarioBase
{
    private const string Channel = "manifold:dims";
    private const double Tolerance = 1e-4;

    // The engine applies a player teleport at once when the OVERWORLD column at the landing X/Z is
    // loaded (whatever the target dimension is), and otherwise queues it until that column loads. The
    // yaw is set from the engine's own completion callback, so both timings are covered. Loading a
    // column also loads its neighbours for generation, so every scenario uses coordinates tens of
    // chunks away from every other one: a column another scenario touched would silently turn a
    // deferred landing into an immediate one. The fixture reports the engine's own Teleporting flag
    // so a scenario fails loudly instead of passing without exercising the timing it names.
    [AtlasScenario]
    public async Task Player_Should_FaceTheRequestedYaw_When_TheEngineDefersTheLanding()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_yaw");
        await World.Ticks(2);
        player.Entity.Pos.Yaw = 0.5f;
        player.Client.Clear();

        Assert.Equal("ok:True", (await Ok("/atlasfx3 teleport-player-yaw-at atlas_yaw flat 2.0 30000 30000")).Message);
        await LandedAt(player, flatId, 30000, 30000);
        await World.Until(() => Math.Abs(player.Entity.Pos.Yaw - 2.0) < Tolerance, timeoutTicks: 600);

        // The player's own client camera is not driven by the server's entity yaw, so the transit
        // notification must carry the requested yaw for the client to turn to.
        PlayerTransitedPacket packet = Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel));
        Assert.Equal(2.0f, packet.Yaw);
    }

    [AtlasScenario]
    public async Task Player_Should_FaceTheRequestedYaw_When_TheEngineAppliesTheLandingAtOnce()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_yawnow");
        await World.Ticks(2);
        player.Entity.Pos.Yaw = 0.5f;
        await LoadOverworldColumn(40000, 40000);

        Assert.Equal("ok:False", (await Ok("/atlasfx3 teleport-player-yaw-at atlas_yawnow flat 2.0 40000 40000")).Message);
        await LandedAt(player, flatId, 40000, 40000);

        await World.Until(() => Math.Abs(player.Entity.Pos.Yaw - 2.0) < Tolerance, timeoutTicks: 600);
    }

    [AtlasScenario]
    public async Task Player_Should_KeepTheirYaw_When_NoYawIsRequested()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_keepyaw");
        await World.Ticks(2);
        player.Entity.Pos.Yaw = 0.75f;
        player.Client.Clear();

        await Ok("/atlasfx teleport-player atlas_keepyaw flat");
        await LandedAt(player, flatId, 512, 512);
        await World.Ticks(10);

        Assert.Equal(0.75f, player.Entity.Pos.Yaw);
        Assert.Null(Assert.Single(player.Client.Packets<PlayerTransitedPacket>(Channel)).Yaw);
    }

    [AtlasScenario]
    public async Task Player_Should_LandAtTheExactSpotAndYaw_When_ReturningTwiceAlongAChain()
    {
        int flatId = await DimensionId("flat");
        int voidId = await DimensionId("void");
        ITestPlayer player = await World.JoinPlayer("atlas_chain");
        await World.Ticks(2);

        // Overworld -> flat, leaving from an exact fractional spot facing a known way.
        var overworldSpot = new Spot(0, player.Entity.Pos.X + 3.25, player.Entity.Pos.Y, player.Entity.Pos.Z - 2.75, 0.9f);
        Place(player, overworldSpot);
        await Ok("/atlasfx teleport-player atlas_chain flat");
        await LandedAt(player, flatId, 512, 512);

        // Flat -> void, again from a spot that is neither block-aligned nor the landing.
        var flatSpot = new Spot(flatId, 520.375, 6.0, 505.625, 2.2f);
        Place(player, flatSpot);
        await Ok("/atlasfx teleport-player atlas_chain void");
        await LandedAt(player, voidId, 512, 512);

        // Each dimension remembers where the player came from, not where the chain started.
        AssertOrigin(await Ok("/atlasfx3 origin atlas_chain"), "atlasfixture:flat", flatSpot);

        // First return: void -> flat, exactly where the player left it. Landing here records nothing.
        Assert.StartsWith("returned:", (await Ok("/atlasfx3 return atlas_chain")).Message);
        await ArrivesAt(player, flatSpot);
        AssertOrigin(await Ok("/atlasfx3 origin atlas_chain"), "manifold:overworld", overworldSpot);

        // Second return: flat -> overworld, at the spot the chain started from.
        Assert.StartsWith("returned:", (await Ok("/atlasfx3 return atlas_chain")).Message);
        await ArrivesAt(player, overworldSpot);

        // The overworld has no origin: a third return is refused and the player stays put.
        Assert.Equal("none", (await Ok("/atlasfx3 origin atlas_chain")).Message);
        Assert.Equal("refused", (await Ok("/atlasfx3 return atlas_chain")).Message);
        await World.Ticks(10);
        Assert.Equal(0, player.Position.dimension);
    }

    [AtlasScenario]
    public async Task Player_Should_LandAtTheExactSpotAndYaw_When_TheReturnIsAppliedAtOnce()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_instant");
        await World.Ticks(2);
        var spot = new Spot(0, player.Entity.Pos.X + 2.125, player.Entity.Pos.Y, player.Entity.Pos.Z + 1.875, 0.4f);
        Place(player, spot);

        // Keep the overworld column the player leaves from loaded, so the way back is applied at once.
        await LoadOverworldColumn((int)spot.X, (int)spot.Z);
        await Ok("/atlasfx3 teleport-player-yaw-at atlas_instant flat 1.0 50000 50000");
        await LandedAt(player, flatId, 50000, 50000);

        Assert.Equal("returned:False", (await Ok("/atlasfx3 return atlas_instant")).Message);
        await ArrivesAt(player, spot);
    }

    // The second transit starts in the same tick, before the engine applied the first, so the entity
    // still reports the coordinates it left. The origin must be where the first transit was taking
    // the player, not those stale coordinates.
    [AtlasScenario]
    public async Task Origin_Should_BeTheFirstLanding_When_APlayerTransitsAgainBeforeItWasApplied()
    {
        int flatId = await DimensionId("flat");
        int voidId = await DimensionId("void");
        ITestPlayer player = await World.JoinPlayer("atlas_hop");
        await World.Ticks(2);
        float yaw = 0.65f;
        player.Entity.Pos.Yaw = yaw;

        Assert.Equal("ok:True", (await Ok("/atlasfx3 double-hop atlas_hop flat 70000 70000 void 80000 80000")).Message);
        var flatLanding = new Spot(flatId, 70000.5, 6, 70000.5, yaw);
        await ArrivesAt(player, new Spot(voidId, 80000.5, 6, 80000.5, yaw));

        AssertOrigin(await Ok("/atlasfx3 origin atlas_hop"), "atlasfixture:flat", flatLanding);

        Assert.StartsWith("returned:", (await Ok("/atlasfx3 return atlas_hop")).Message);
        await ArrivesAt(player, flatLanding);
    }

    [AtlasScenario]
    public async Task Origin_Should_BeWrittenToTheSaveGame_When_APlayerTransitsAndTheWorldSaves()
    {
        int flatId = await DimensionId("flat");
        ITestPlayer player = await World.JoinPlayer("atlas_save");
        await World.Ticks(2);
        var spot = new Spot(0, player.Entity.Pos.X + 1.5, player.Entity.Pos.Y, player.Entity.Pos.Z + 0.25, 1.1f);
        Place(player, spot);
        await Ok("/atlasfx teleport-player atlas_save flat");
        await LandedAt(player, flatId, 512, 512);

        await Ok("/autosavenow");
        await World.Until(() => World.Api.WorldManager.SaveGame.GetData("manifold:origins") is { Length: > 0 }, timeoutTicks: 600);

        // Read back what a real world save wrote, through the same schema version the sidecar records.
        var sidecar = SchemaSidecar.Load(World.Api.WorldManager.SaveGame.GetData("manifold:schema"));
        Assert.Equal(PlayerOriginStore.SchemaVersion, sidecar.GetVersion("manifold:origins"));
        var store = new PlayerOriginStore();
        store.LoadFromBytes(World.Api.WorldManager.SaveGame.GetData("manifold:origins"), version: sidecar.GetVersion("manifold:origins"));
        Assert.True(store.TryGet(player.Player.PlayerUID, flatId, out OriginEntry origin));
        Assert.Equal("manifold:overworld", origin.SourceCode);
        Assert.Equal(0, origin.SourceId);
        Assert.Equal(spot.X, origin.X, Tolerance);
        Assert.Equal(spot.Z, origin.Z, Tolerance);
        Assert.Equal(spot.Yaw, origin.Yaw, (float)Tolerance);
    }

    /// <summary>Loads, and keeps loaded, the overworld column at a block X/Z (see the fixture's <c>load-column</c>) and waits until it is in.</summary>
    private async Task LoadOverworldColumn(int x, int z)
    {
        await Ok($"/atlasfx3 load-column {x} {z}");
        string key = $"atlasfixture:column:{x / 32}:{z / 32}";
        await World.Until(() => FlagIsSet(key), timeoutTicks: 600);
    }

    /// <summary>Puts the live server-side entity at an exact position and yaw (the documented <c>ITestPlayer.Entity</c> escape hatch).</summary>
    private static void Place(ITestPlayer player, Spot spot)
    {
        player.Entity.Pos.SetPos(spot.X, spot.Y, spot.Z);
        player.Entity.Pos.Yaw = spot.Yaw;
    }

    /// <summary>Waits until the player stands, to the tolerance, exactly on <paramref name="spot"/> in its dimension, facing its yaw.</summary>
    private Task ArrivesAt(ITestPlayer player, Spot spot) =>
        World.Until(
            () => player.Entity.Pos.Dimension == spot.Dimension
                && Math.Abs(player.Entity.Pos.X - spot.X) < Tolerance
                && Math.Abs(player.Entity.Pos.Y - spot.Y) < Tolerance
                && Math.Abs(player.Entity.Pos.Z - spot.Z) < Tolerance
                && Math.Abs(player.Entity.Pos.Yaw - spot.Yaw) < Tolerance,
            timeoutTicks: 600);

    private static void AssertOrigin(CommandResult result, string dimension, Spot spot)
    {
        string[] parts = result.Message.Split('|');
        Assert.Equal(5, parts.Length);
        Assert.Equal(dimension, parts[0]);
        Assert.Equal(spot.X, double.Parse(parts[1], CultureInfo.InvariantCulture), Tolerance);
        Assert.Equal(spot.Y, double.Parse(parts[2], CultureInfo.InvariantCulture), Tolerance);
        Assert.Equal(spot.Z, double.Parse(parts[3], CultureInfo.InvariantCulture), Tolerance);
        Assert.Equal(spot.Yaw, float.Parse(parts[4], CultureInfo.InvariantCulture), (float)Tolerance);
    }

    private sealed record Spot(int Dimension, double X, double Y, double Z, float Yaw);
}
