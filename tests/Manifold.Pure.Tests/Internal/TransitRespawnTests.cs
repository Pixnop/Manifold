using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Transitions;
using Manifold.Internal;
using Manifold.Pure.Tests.Fakes;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace Manifold.Pure.Tests.Internal;

/// <summary>
/// <see cref="TransitService.RespawnPlayer"/>: a player the engine has just respawned (alive again,
/// standing at the spawn coordinates it chose, still in the dimension they died in) is taken out of
/// that dimension, or kept in it at its fixed spawn when it asked for that.
/// </summary>
public sealed class TransitRespawnTests
{
    private static readonly AssetLocation Plain = new("owner:plain");
    private static readonly AssetLocation Keeper = new("owner:keeper");
    private static readonly AssetLocation NoSpawn = new("owner:nospawn");
    private static readonly AssetLocation Forced = new("owner:forced");
    private static readonly AssetLocation Later = new("owner:later");
    private static readonly string[] LeftThenEntered = ["left:plain->overworld", "entered:overworld"];
    private static readonly string[] LeftThenEnteredFromNowhere = ["left:overworld->overworld", "entered:overworld"];
    private static readonly string[] LeftThenEnteredForced = ["left:plain->forced", "entered:forced"];

    [Fact]
    public void RespawnPlayer_Should_Do_Nothing_When_The_Player_Is_In_The_Overworld()
    {
        var fx = NewFixture();
        fx.Stand(0, 7.5, 64, 8.5);
        var events = fx.RecordEvents();

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        Assert.False(moved);
        Assert.Empty(events);
        Assert.Equal(0, fx.Teleporter.ExactCalls + fx.Teleporter.BlockCalls);
    }

    [Fact]
    public void RespawnPlayer_Should_Move_The_Player_To_The_Overworld_At_The_Engine_Position_When_They_Died_In_A_Dimension()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 512019.5, 3, 512017.5);

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.True(moved);
        Assert.Equal(0, pos.Dimension);
        Assert.Equal((512019.5, 3.0, 512017.5), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void RespawnPlayer_Should_Raise_Left_Then_Entered_And_Neither_Entering_Nor_Arriving()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 10.5, 3, 11.5);
        var events = fx.RecordEvents();

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(LeftThenEntered, events);
    }

    [Fact]
    public void RespawnPlayer_Should_Report_The_Landing_Block_And_No_Yaw_On_PlayerEntered()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 10.5, 3, -11.5);
        Manifold.Api.Events.PlayerEnteredDimensionEventArgs? captured = null;
        fx.Service.PlayerEntered += (_, e) => captured = e;

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(new BlockPos(10, 3, -12, 0), captured!.TargetPosition);
        Assert.Equal(fx.IdOf(Plain), captured.SourceDimension.InternalId);
        Assert.Null(captured.Yaw);
        Assert.Null(fx.Teleporter.LastYaw);
    }

    [Fact]
    public void RespawnPlayer_Should_Not_Ask_Whether_The_Respawn_Is_Allowed()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Forced), 10.5, 3, 11.5);
        fx.Service.PlayerEntering += (_, e) => e.Cancel = true;
        fx.Service.PlayerArriving += (_, e) => e.Cancel = true;

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        // A respawn cannot be refused: a veto would leave the player stuck in the dimension.
        Assert.True(moved);
        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
    }

    [Fact]
    public void RespawnPlayer_Should_Hand_Back_The_Game_Mode_The_Dimension_Forced()
    {
        var fx = NewFixture();
        fx.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        fx.Stand(0, 1, 64, 1);
        fx.Service.TeleportPlayer(fx.Player, Forced);
        Assert.Equal(EnumGameMode.Creative, fx.Player.WorldData.CurrentGameMode);

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(EnumGameMode.Survival, fx.Player.WorldData.CurrentGameMode);
    }

    [Fact]
    public void RespawnPlayer_Should_Not_Touch_The_Game_Mode_When_The_Dimension_Forced_None()
    {
        var fx = NewFixture();
        fx.Player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        fx.Stand(fx.IdOf(Plain), 1, 3, 1);

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(EnumGameMode.Creative, fx.Player.WorldData.CurrentGameMode);
    }

    [Fact]
    public void RespawnPlayer_Should_Record_Neither_An_Origin_Nor_A_Last_Visited_Position()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 512019.5, 3, 512017.5);

        fx.Service.RespawnPlayer(fx.Player);

        // The engine's spawn coordinates are not a place the player walked to: a last-visited
        // position made of them would drop the player in the void of a dimension next time.
        Assert.False(fx.Store.TryGet(fx.Player.PlayerUID, fx.IdOf(Plain), out _, out _, out _));
        Assert.False(fx.Store.Origins.TryGet(fx.Player.PlayerUID, 0, out _));
        Assert.False(fx.Store.Origins.TryGet(fx.Player.PlayerUID, fx.IdOf(Plain), out _));
    }

    [Fact]
    public void RespawnPlayer_Should_Not_Try_To_Dismount_The_Player()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 1, 3, 1);

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        // Death already unmounted them, so a respawn has nothing to release (and must not be able to abort).
        Assert.True(moved);
        fx.Dismounter.DidNotReceive().Dismount(Arg.Any<IServerPlayer>());
    }

    [Fact]
    public void RespawnPlayer_Should_Put_The_Player_At_The_Fixed_Spawn_Without_Leaving_When_The_Dimension_Keeps_Its_Dead()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Keeper), 512019.5, 3, 512017.5);
        var events = fx.RecordEvents();

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.True(moved);
        Assert.Equal(fx.IdOf(Keeper), pos.Dimension);
        Assert.Equal((50.5, 70.0, 60.5), (pos.X, pos.Y, pos.Z));
        Assert.Empty(events);
    }

    [Fact]
    public void RespawnPlayer_Should_Leave_The_Game_Mode_Alone_When_The_Dimension_Keeps_Its_Dead()
    {
        var fx = NewFixture();
        fx.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        fx.Stand(0, 1, 64, 1);
        fx.Service.TeleportPlayer(fx.Player, Keeper);
        fx.Player.WorldData.CurrentGameMode = EnumGameMode.Spectator;

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(EnumGameMode.Spectator, fx.Player.WorldData.CurrentGameMode);
    }

    [Fact]
    public void RespawnPlayer_Should_Fall_Back_To_The_Overworld_And_Warn_Once_When_The_Dimension_Has_No_Spawn_Point()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(NoSpawn), 20.5, 3, 21.5);

        fx.Service.RespawnPlayer(fx.Player);
        fx.Stand(fx.IdOf(NoSpawn), 20.5, 3, 21.5);
        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
        fx.Sapi.Logger.Received(1).Warning(
            Arg.Is<string>(m => m.Contains("RespawnBehavior.DimensionSpawn")), Arg.Any<object[]>());
    }

    [Fact]
    public void RespawnPlayer_Should_Move_The_Player_To_The_Overworld_When_Their_Dimension_Is_Not_Registered()
    {
        var fx = NewFixture();
        fx.Stand(777, 20.5, 3, 21.5);
        var events = fx.RecordEvents();

        bool moved = fx.Service.RespawnPlayer(fx.Player);

        Assert.True(moved);
        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
        Assert.Equal(LeftThenEnteredFromNowhere, events);
    }

    [Fact]
    public void RespawnPlayer_Should_Move_The_Player_To_The_Overworld_When_Their_Dimension_Is_Only_Pending()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Later), 20.5, 3, 21.5);

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
    }

    [Fact]
    public void RespawnPlayer_Should_Land_In_The_Dimension_The_Spawn_Y_Designates_And_Apply_Its_Policies()
    {
        var fx = NewFixture();
        fx.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        fx.Stand(fx.IdOf(Plain), 30.5, (fx.IdOf(Forced) * TransitService.DimensionYStride) + 6, 31.5);

        fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.Equal(fx.IdOf(Forced), pos.Dimension);
        Assert.Equal((30.5, 6.0, 31.5), (pos.X, pos.Y, pos.Z));
        Assert.Equal(EnumGameMode.Creative, fx.Player.WorldData.CurrentGameMode);
    }

    [Fact]
    public void RespawnPlayer_Should_Raise_Left_And_Entered_For_The_Designated_Dimension()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 30.5, (fx.IdOf(Forced) * TransitService.DimensionYStride) + 6, 31.5);
        var events = fx.RecordEvents();

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(LeftThenEnteredForced, events);
    }

    [Fact]
    public void RespawnPlayer_Should_Only_Fix_The_Y_Without_Events_When_The_Spawn_Y_Designates_The_Dimension_They_Died_In()
    {
        var fx = NewFixture();
        int plainId = fx.IdOf(Plain);
        fx.Stand(plainId, 30.5, (plainId * TransitService.DimensionYStride) + 6, 31.5);
        var events = fx.RecordEvents();

        fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.Equal(plainId, pos.Dimension);
        Assert.Equal((30.5, 6.0, 31.5), (pos.X, pos.Y, pos.Z));
        Assert.Empty(events);
    }

    [Fact]
    public void RespawnPlayer_Should_Land_At_The_World_Spawn_When_The_Designated_Dimension_Is_Not_Active()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 30.5, (fx.IdOf(Later) * TransitService.DimensionYStride) + 6, 31.5);

        fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.Equal(0, pos.Dimension);
        Assert.Equal((1000.5, 7.0, 2000.5), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void RespawnPlayer_Should_Keep_The_Engine_Coordinates_When_The_Designated_Dimension_Is_Gone_And_There_Is_No_World_Spawn()
    {
        var fx = NewFixture();
        fx.Sapi.World.DefaultSpawnPosition.Returns((EntityPos?)null);
        fx.Stand(fx.IdOf(Plain), 30.5, (777 * TransitService.DimensionYStride) + 6, 31.5);

        fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.Equal(0, pos.Dimension);
        Assert.Equal((30.5, 6.0, 31.5), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void RespawnPlayer_Should_Prefer_The_Dimensions_Fixed_Spawn_Over_A_Designated_Dimension_When_It_Keeps_Its_Dead()
    {
        var fx = NewFixture();
        int keeperId = fx.IdOf(Keeper);
        fx.Stand(keeperId, 30.5, (fx.IdOf(Forced) * TransitService.DimensionYStride) + 6, 31.5);

        fx.Service.RespawnPlayer(fx.Player);

        var pos = fx.Player.Entity.Pos;
        Assert.Equal(keeperId, pos.Dimension);
        Assert.Equal((50.5, 70.0, 60.5), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void RespawnPlayer_Should_Leave_The_Player_At_The_Respawn_Landing_When_An_Earlier_Transit_Lands_Late()
    {
        var fx = NewFixture();
        fx.Stand(0, 5.5, 70, 6.5);

        // The player stepped into a dimension: the engine has not applied that move yet (its column is
        // not loaded) when they die there and the engine's respawn puts them at the spawn.
        fx.Teleporter.Defer = true;
        fx.Service.TeleportPlayer(fx.Player, Plain, new TransitionOptions { OverridePosition = new BlockPos(1000, 70, 1000, 0) });
        var pos = fx.Player.Entity.Pos;
        pos.SetPos(40.5, 3, 41.5);
        fx.Teleporter.Defer = false;
        fx.Service.RespawnPlayer(fx.Player);

        fx.Teleporter.LandNext(); // the engine finally runs the first transit's move, at the old coordinates

        Assert.Equal(0, pos.Dimension);
        Assert.Equal((40.5, 3.0, 41.5), (pos.X, pos.Y, pos.Z));
        Assert.Null(fx.Teleporter.GetPendingLanding(fx.Player));
    }

    [Fact]
    public void RespawnPlayer_Should_Apply_At_Once_Or_Late_The_Same_Way()
    {
        var fx = NewFixture();
        fx.Teleporter.Defer = true;
        fx.Stand(fx.IdOf(Plain), 40.5, 3, 41.5);

        fx.Service.RespawnPlayer(fx.Player);
        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
        fx.Teleporter.LandAll();

        var pos = fx.Player.Entity.Pos;
        Assert.Equal((40.5, 3.0, 41.5), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void RespawnPlayer_Should_Still_Move_The_Player_When_A_PlayerLeft_Subscriber_Throws()
    {
        var fx = NewFixture();
        fx.Stand(fx.IdOf(Plain), 40.5, 3, 41.5);
        bool entered = false;
        fx.Service.PlayerLeft += (_, _) => throw new System.InvalidOperationException("faulty subscriber");
        fx.Service.PlayerEntered += (_, _) => entered = true;

        fx.Service.RespawnPlayer(fx.Player);

        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
        Assert.True(entered);
    }

    [Theory]
    [InlineData(64.0, 0, 64.0)]
    [InlineData(0.0, 0, 0.0)]
    [InlineData(32767.5, 0, 32767.5)]
    [InlineData(32768.0, 1, 0.0)]
    [InlineData(327683.0, 10, 3.0)]
    [InlineData(327720.0, 10, 40.0)]
    public void DecodeSpawnY_Should_Split_The_Dimension_From_The_Local_Y(double rawY, int dimension, double y)
    {
        var decoded = TransitService.DecodeSpawnY(rawY);

        Assert.Equal(dimension, decoded.Dimension);
        Assert.Equal(y, decoded.Y);
    }

    private static Fixture NewFixture()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        Define(registry, Plain, b => b);
        Define(registry, Keeper, b => b.WithFixedSpawn(new BlockPos(50, 70, 60, 0)).WithRespawnBehavior(RespawnBehavior.DimensionSpawn));
        Define(registry, NoSpawn, b => b.WithRespawnBehavior(RespawnBehavior.DimensionSpawn));
        Define(registry, Forced, b => b.WithForcedGameMode(EnumGameMode.Creative));
        registry.SeedFromManifest(new ManifestEntry(Later, 40, DimensionLifetime.Persistent, "owner"), DimensionState.Pending);

        var resolver = Substitute.For<ITargetPositionResolver>();
        resolver.Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(_ => new BlockPos(100, 100, 100, 0));
        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.Logger.Returns(Substitute.For<ILogger>());
        sapi.World.DefaultSpawnPosition.Returns(new EntityPos(1000.5, 7, 2000.5));
        var teleporter = new MovingPlayerTeleporter();
        var dismounter = Substitute.For<IPlayerDismounter>();
        dismounter.Dismount(Arg.Any<IServerPlayer>()).Returns(true);
        var store = new PlayerPositionStore();
        var service = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, Substitute.For<IEntityMover>(), Substitute.For<IBlockMover>(), dismounter),
            resolver,
            new DimensionGenerator(registry, new GeneratedColumnStore()),
            store,
            new InventorySwapper(sapi));
        return new Fixture(service, registry, store, teleporter, dismounter, sapi, NewPlayer());
    }

    private static void Define(DimensionRegistry registry, AssetLocation code, System.Func<Manifold.Api.Server.IDimensionBuilder, Manifold.Api.Server.IDimensionBuilder> configure) =>
        configure(registry.DefineForOwner(code, "owner").WithWorldgen(new FakeWorldgenStrategy())).RegisterStatic();

    private static IServerPlayer NewPlayer()
    {
        var entity = Substitute.For<EntityPlayer>();
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        player.PlayerUID.Returns("alice");
        player.PlayerName.Returns("alice");

        // A bare substitute returns an empty array for moddata, not null: back it with a dictionary.
        var moddata = new Dictionary<string, byte[]>();
        player.GetModdata(Arg.Any<string>()).Returns(c => moddata.TryGetValue(c.Arg<string>(), out var v) ? v : null);
        player.When(p => p.SetModdata(Arg.Any<string>(), Arg.Any<byte[]>())).Do(c => moddata[c.ArgAt<string>(0)] = c.ArgAt<byte[]>(1));
        player.When(p => p.RemoveModdata(Arg.Any<string>())).Do(c => moddata.Remove(c.Arg<string>()));
        return player;
    }

    private sealed record Fixture(
        TransitService Service,
        DimensionRegistry Registry,
        PlayerPositionStore Store,
        MovingPlayerTeleporter Teleporter,
        IPlayerDismounter Dismounter,
        ICoreServerAPI Sapi,
        IServerPlayer Player)
    {
        public int IdOf(AssetLocation code) => Registry.Get(code)!.InternalId;

        /// <summary>Puts the player where the engine's respawn teleport left them: at the spawn coordinates, in the dimension they died in.</summary>
        public void Stand(int dimension, double x, double y, double z)
        {
            var pos = Player.Entity.Pos;
            pos.Dimension = dimension;
            pos.SetPos(x, y, z);
        }

        /// <summary>The transit events raised from now on, as "left:source->target" / "entered:target" / "entering:target" / "arriving:target".</summary>
        public List<string> RecordEvents()
        {
            var events = new List<string>();
            Service.PlayerEntering += (_, e) => events.Add("entering:" + e.TargetDimension.Code.Path);
            Service.PlayerArriving += (_, e) => events.Add("arriving:" + e.TargetDimension.Code.Path);
            Service.PlayerLeft += (_, e) => events.Add($"left:{e.SourceDimension.Code.Path}->{e.TargetDimension.Code.Path}");
            Service.PlayerEntered += (_, e) => events.Add("entered:" + e.TargetDimension.Code.Path);
            return events;
        }
    }
}
