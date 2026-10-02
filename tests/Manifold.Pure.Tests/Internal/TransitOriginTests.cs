using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Events;
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

/// <summary>Arrival yaw and return-to-origin behavior of <see cref="TransitService"/>.</summary>
public sealed class TransitOriginTests
{
    private static readonly AssetLocation A = new("owner:a");
    private static readonly AssetLocation B = new("owner:b");

    [Fact]
    public void TeleportPlayer_Should_Forward_The_Yaw_Option_To_The_Teleporter()
    {
        var fx = NewFixture();

        fx.Service.TeleportPlayer(fx.Player, A, new TransitionOptions { Yaw = 1.25f });

        Assert.Equal(1.25f, fx.Teleporter.LastYaw);
    }

    [Fact]
    public void TeleportPlayer_Should_Leave_The_Yaw_Alone_When_No_Yaw_Is_Requested()
    {
        var fx = NewFixture();
        fx.Player.Entity.Pos.Yaw = 2f;

        fx.Service.TeleportPlayer(fx.Player, A);

        Assert.Null(fx.Teleporter.LastYaw);
        Assert.Equal(2f, fx.Player.Entity.Pos.Yaw);
    }

    [Fact]
    public void TeleportPlayer_Should_Report_The_Yaw_On_PlayerEntered()
    {
        var fx = NewFixture();
        PlayerEnteredDimensionEventArgs? captured = null;
        fx.Service.PlayerEntered += (_, e) => captured = e;

        fx.Service.TeleportPlayer(fx.Player, A, new TransitionOptions { Yaw = 0.5f });

        Assert.Equal(0.5f, captured!.Yaw);
    }

    [Fact]
    public void TeleportPlayer_Should_Report_No_Yaw_On_PlayerEntered_When_None_Is_Requested()
    {
        var fx = NewFixture();
        PlayerEnteredDimensionEventArgs? captured = null;
        fx.Service.PlayerEntered += (_, e) => captured = e;

        fx.Service.TeleportPlayer(fx.Player, A);

        Assert.Null(captured!.Yaw);
    }

    [Fact]
    public void GetOrigin_Should_Return_Null_When_The_Player_Never_Transited()
    {
        var fx = NewFixture();

        Assert.Null(fx.Service.GetOrigin(fx.Player));
    }

    [Fact]
    public void GetOrigin_Should_Return_The_Exact_Position_And_Yaw_The_Player_Left()
    {
        var fx = NewFixture();
        fx.Stand(0, 10.25, 64.5, -3.75, 1.5f);

        fx.Service.TeleportPlayer(fx.Player, A);
        var origin = fx.Service.GetOrigin(fx.Player);

        Assert.NotNull(origin);
        Assert.Equal(0, origin!.Dimension.InternalId);
        Assert.Equal(10.25, origin.X);
        Assert.Equal(64.5, origin.Y);
        Assert.Equal(-3.75, origin.Z);
        Assert.Equal(new Vec3d(10.25, 64.5, -3.75), origin.Position);
        Assert.Equal(1.5f, origin.Yaw);
    }

    [Fact]
    public void GetOrigin_Should_Be_Recorded_Per_Player()
    {
        var fx = NewFixture();
        var other = fx.NewPlayer("bob", 0, 1, 2, 3, 0f);
        fx.Stand(0, 10, 64, 10, 1f);

        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.TeleportPlayer(other, A);

        Assert.Equal(10, fx.Service.GetOrigin(fx.Player)!.X);
        Assert.Equal(1, fx.Service.GetOrigin(other)!.X);
    }

    [Fact]
    public void TeleportPlayer_Should_Not_Record_An_Origin_When_The_Player_Stays_In_The_Same_Dimension()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.TeleportPlayer(fx.Player, B);
        var originInB = fx.Service.GetOrigin(fx.Player);

        fx.Service.TeleportPlayer(fx.Player, B); // already in B

        Assert.Equal(originInB, fx.Service.GetOrigin(fx.Player));
    }

    [Fact]
    public void TeleportPlayer_Should_Not_Record_An_Origin_When_The_Transit_Is_Cancelled()
    {
        var fx = NewFixture();
        fx.Service.PlayerArriving += (_, e) => e.Cancel = true;

        fx.Service.TeleportPlayer(fx.Player, A);

        Assert.Null(fx.StoreOrigin(A));
    }

    [Fact]
    public void TeleportPlayer_Should_Not_Record_An_Origin_When_The_Player_Cannot_Be_Dismounted()
    {
        var fx = NewFixture();
        fx.Dismounter.Dismount(Arg.Any<IServerPlayer>()).Returns(false);

        fx.Service.TeleportPlayer(fx.Player, A);

        Assert.Null(fx.StoreOrigin(A));
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_When_Nothing_Is_Recorded()
    {
        var fx = NewFixture();
        var entered = false;
        fx.Service.PlayerEntered += (_, _) => entered = true;

        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
        Assert.False(entered);
        Assert.Equal(0, fx.Teleporter.ExactCalls);
    }

    [Fact]
    public void TryReturnPlayer_Should_Land_At_The_Exact_Origin_Position_Dimension_And_Yaw()
    {
        var fx = NewFixture();
        fx.Stand(0, 10.25, 64.5, -3.75, 1.5f);
        fx.Service.TeleportPlayer(fx.Player, A);

        bool returned = fx.Service.TryReturnPlayer(fx.Player);

        Assert.True(returned);
        var pos = fx.Player.Entity.Pos;
        Assert.Equal(0, pos.Dimension);
        Assert.Equal(10.25, pos.X);
        Assert.Equal(64.5, pos.Y);
        Assert.Equal(-3.75, pos.Z);
        Assert.Equal(1.5f, pos.Yaw);
        Assert.Equal(1, fx.Teleporter.ExactCalls);
    }

    [Fact]
    public void TryReturnPlayer_Should_Unwind_A_Chain_One_Step_At_A_Time()
    {
        var fx = NewFixture();
        fx.Stand(0, 5.5, 70, 6.5, 0.5f);
        fx.Service.TeleportPlayer(fx.Player, A);
        int aId = fx.Registry.Get(A)!.InternalId;
        fx.Stand(aId, 100.125, 80.5, 200.875, 2.5f);
        fx.Service.TeleportPlayer(fx.Player, B);

        Assert.True(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Equal(aId, fx.Player.Entity.Pos.Dimension);
        Assert.Equal(100.125, fx.Player.Entity.Pos.X);
        Assert.Equal(200.875, fx.Player.Entity.Pos.Z);
        Assert.Equal(2.5f, fx.Player.Entity.Pos.Yaw);

        Assert.True(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Equal(0, fx.Player.Entity.Pos.Dimension);
        Assert.Equal(5.5, fx.Player.Entity.Pos.X);
        Assert.Equal(6.5, fx.Player.Entity.Pos.Z);
        Assert.Equal(0.5f, fx.Player.Entity.Pos.Yaw);

        Assert.False(fx.Service.TryReturnPlayer(fx.Player)); // the overworld has no origin
    }

    [Fact]
    public void TryReturnPlayer_Should_Not_Record_A_New_Origin_For_The_Dimension_It_Lands_In()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.TeleportPlayer(fx.Player, B);

        Assert.True(fx.Service.TryReturnPlayer(fx.Player)); // B -> A

        // A's origin is still the overworld, not B: returning from A does not ping-pong.
        Assert.Equal(0, fx.Service.GetOrigin(fx.Player)!.Dimension.InternalId);
    }

    [Fact]
    public void TryReturnPlayer_Should_Raise_The_Transit_Events_With_The_Origin_Block_And_Yaw()
    {
        var fx = NewFixture();
        fx.Stand(0, -10.5, 64.25, 7.75, 3f);
        fx.Service.TeleportPlayer(fx.Player, A);
        var order = new List<string>();
        PlayerEnteredDimensionEventArgs? entered = null;
        fx.Service.PlayerEntering += (_, _) => order.Add("entering");
        fx.Service.PlayerArriving += (_, _) => order.Add("arriving");
        fx.Service.PlayerLeft += (_, _) => order.Add("left");
        fx.Service.PlayerEntered += (_, e) =>
        {
            order.Add("entered");
            entered = e;
        };

        fx.Service.TryReturnPlayer(fx.Player);

        Assert.Equal(new[] { "entering", "arriving", "left", "entered" }, order);
        Assert.Equal(new BlockPos(-11, 64, 7, 0), entered!.TargetPosition);
        Assert.Equal(3f, entered.Yaw);
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_And_Keep_The_Origin_When_A_Subscriber_Cancels()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.PlayerEntering += (_, e) => e.Cancel = true;

        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Equal(0, fx.Teleporter.ExactCalls);
        Assert.NotNull(fx.Service.GetOrigin(fx.Player));
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_When_The_Player_Cannot_Be_Dismounted()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Dismounter.Dismount(Arg.Any<IServerPlayer>()).Returns(false);

        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Equal(0, fx.Teleporter.ExactCalls);
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_When_The_Origin_Dimension_No_Longer_Exists()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.TeleportPlayer(fx.Player, B); // origin of B is A
        fx.Registry.Purge(A);

        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Null(fx.Service.GetOrigin(fx.Player));
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_When_A_Recycled_Id_Now_Names_Another_Dimension()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        int bId = fx.Registry.Get(B)!.InternalId;

        // An origin whose id is B's but whose recorded code is some long-gone dimension.
        fx.Store.Origins.Record(fx.Player.PlayerUID, fx.Player.Entity.Pos.Dimension, new OriginEntry(bId, "gone:dim", 1, 2, 3, 0f));

        Assert.Null(fx.Service.GetOrigin(fx.Player));
        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
    }

    [Fact]
    public void TryReturnPlayer_Should_Return_False_When_The_Origin_Dimension_Is_Not_Active()
    {
        var fx = NewFixture();
        fx.Service.TeleportPlayer(fx.Player, A);
        var pending = new AssetLocation("ghost:pending");
        fx.Registry.SeedFromManifest(new ManifestEntry(pending, 77, DimensionLifetime.Persistent, "ghost"), DimensionState.Pending);
        fx.Store.Origins.Record(fx.Player.PlayerUID, fx.Player.Entity.Pos.Dimension, new OriginEntry(77, "ghost:pending", 1, 2, 3, 0f));

        Assert.False(fx.Service.TryReturnPlayer(fx.Player));
        Assert.Equal(0, fx.Teleporter.ExactCalls);
    }

    [Fact]
    public void TryReturnPlayer_Should_Log_Which_Case_Happened_At_Notification_Level()
    {
        var fx = NewFixture();

        fx.Service.TryReturnPlayer(fx.Player); // nothing recorded
        fx.Service.TeleportPlayer(fx.Player, A);
        fx.Service.TryReturnPlayer(fx.Player); // success

        fx.Sapi.Logger.Received(1).Notification(Arg.Is<string>(m => m.Contains("no origin is recorded")), Arg.Any<object[]>());
        fx.Sapi.Logger.Received(1).Notification(Arg.Is<string>(m => m.Contains("Returned")), Arg.Any<object[]>());
    }

    [Fact]
    public void TryReturnPlayer_Should_Throw_When_Player_Is_Null()
    {
        var fx = NewFixture();

        Assert.Throws<System.ArgumentNullException>(() => fx.Service.TryReturnPlayer(null!));
        Assert.Throws<System.ArgumentNullException>(() => fx.Service.GetOrigin(null!));
    }

    private static Fixture NewFixture()
    {
        var registry = new DimensionRegistry(new DimensionAllocator());
        foreach (var code in new[] { A, B })
        {
            registry.DefineForOwner(code, "owner").WithWorldgen(new FakeWorldgenStrategy()).RegisterStatic();
        }

        var resolver = Substitute.For<ITargetPositionResolver>();
        resolver.Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(_ => new BlockPos(100, 100, 100, 0));
        var sapi = Substitute.For<ICoreServerAPI>();
        sapi.Logger.Returns(Substitute.For<ILogger>());
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
        return new Fixture(service, registry, store, teleporter, dismounter, sapi, NewPlayerAt("alice", 0, 0, 64, 0, 0f));
    }

    private static IServerPlayer NewPlayerAt(string uid, int dimension, double x, double y, double z, float yaw)
    {
        var entity = Substitute.For<EntityPlayer>();
        entity.Pos.Dimension = dimension;
        entity.Pos.SetPos(x, y, z);
        entity.Pos.Yaw = yaw;
        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(entity);
        player.PlayerUID.Returns(uid);
        player.PlayerName.Returns(uid);
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
        public IServerPlayer NewPlayer(string uid, int dimension, double x, double y, double z, float yaw) =>
            NewPlayerAt(uid, dimension, x, y, z, yaw);

        public void Stand(int dimension, double x, double y, double z, float yaw)
        {
            var pos = Player.Entity.Pos;
            pos.Dimension = dimension;
            pos.SetPos(x, y, z);
            pos.Yaw = yaw;
        }

        public OriginEntry? StoreOrigin(AssetLocation destination) =>
            Store.Origins.TryGet(Player.PlayerUID, Registry.Get(destination)!.InternalId, out var origin) ? origin : null;
    }
}
