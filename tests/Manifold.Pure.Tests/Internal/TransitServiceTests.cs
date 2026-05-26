using System;
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

public sealed class TransitServiceTests
{
    private static readonly string[] LeftThenEntered = ["left", "entered"];

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, player, _, _, _) = NewService();
        Assert.Throws<DimensionNotFoundException>(
            () => svc.TeleportPlayer(player, Code("nope:nope")));
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerEntering()
    {
        var (svc, _, player, _, _, _) = NewService();
        bool entered = false;
        svc.PlayerEntering += (_, _) => entered = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.True(entered);
    }

    [Fact]
    public void TeleportPlayer_Should_Abort_When_PlayerEntering_Cancelled()
    {
        var (svc, _, player, tele, _, _) = NewService();
        bool entered = false;
        bool left = false;
        svc.PlayerEntering += (_, e) => e.Cancel = true;
        svc.PlayerEntered += (_, _) => entered = true;
        svc.PlayerLeft += (_, _) => left = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.False(entered);
        Assert.False(left);
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_Left_Then_Entered_In_Order()
    {
        var (svc, _, player, _, _, _) = NewService();
        var order = new System.Collections.Generic.List<string>();
        svc.PlayerLeft += (_, _) => order.Add("left");
        svc.PlayerEntered += (_, _) => order.Add("entered");
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(LeftThenEntered, order);
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_PlayerArriving_With_Final_Position()
    {
        var (svc, _, player, _, _, _) = NewService();
        PlayerArrivingDimensionEventArgs? captured = null;
        svc.PlayerArriving += (_, e) => captured = e;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.NotNull(captured);
        Assert.Same(player, captured!.Player);
        Assert.Equal("owner:target", captured.TargetDimension.Code.ToString());
        Assert.Equal(new BlockPos(100, 100, 100, captured.TargetDimension.InternalId), captured.TargetPosition);
    }

    [Fact]
    public void TeleportPlayer_Should_Raise_Entering_Then_Arriving_In_Order()
    {
        var (svc, _, player, _, _, _) = NewService();
        var order = new System.Collections.Generic.List<string>();
        svc.PlayerEntering += (_, _) => order.Add("entering");
        svc.PlayerArriving += (_, _) => order.Add("arriving");
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.Equal(new[] { "entering", "arriving" }, order);
    }

    [Fact]
    public void TeleportPlayer_Should_Abort_When_PlayerArriving_Cancelled()
    {
        var (svc, _, player, tele, _, _) = NewService();
        bool entered = false;
        bool left = false;
        svc.PlayerArriving += (_, e) => e.Cancel = true;
        svc.PlayerEntered += (_, _) => entered = true;
        svc.PlayerLeft += (_, _) => left = true;
        svc.TeleportPlayer(player, Code("owner:target"));
        Assert.False(entered);
        Assert.False(left);
        tele.DidNotReceive().Teleport(Arg.Any<IServerPlayer>(), Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Call_Teleporter_When_Not_Cancelled()
    {
        var (svc, _, player, tele, _, _) = NewService();
        svc.TeleportPlayer(player, Code("owner:target"));
        tele.Received(1).Teleport(player, Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Manifold_Unhealthy()
    {
        var (svc, _, player, _, _, _) = NewService();
        svc.MarkUnhealthy();
        Assert.Throws<ManifoldUnhealthyException>(
            () => svc.TeleportPlayer(player, Code("owner:target")));
    }

    [Fact]
    public void TeleportPlayer_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, player, _, _, _) = NewService();
        var qCode = Code("ghost:dim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 99, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        Assert.Throws<DimensionStateException>(
            () => svc.TeleportPlayer(player, qCode));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Entity_Is_A_Player()
    {
        var (svc, _, _, _, _, _) = NewService();
        var playerEntity = Substitute.For<EntityPlayer>();
        Assert.Throws<ArgumentException>(() => svc.TeleportEntity(playerEntity, Code("owner:target")));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        Assert.Throws<DimensionNotFoundException>(() => svc.TeleportEntity(entity, Code("nope:nope")));
    }

    [Fact]
    public void TeleportEntity_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, _, _, _, _) = NewService();
        var qCode = Code("ghost:edim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 98, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        var entity = Substitute.For<Entity>();
        Assert.Throws<DimensionStateException>(() => svc.TeleportEntity(entity, qCode));
    }

    [Fact]
    public void TeleportEntity_Should_Call_Mover_With_Resolved_Position()
    {
        var (svc, _, _, _, mover, _) = NewService();
        var entity = Substitute.For<Entity>();
        svc.TeleportEntity(entity, Code("owner:target"));
        mover.Received(1).Move(entity, Arg.Any<BlockPos>());
    }

    [Fact]
    public void TeleportEntity_Should_Raise_EntityChangedDimension_With_Final_Position()
    {
        var (svc, _, _, _, _, _) = NewService();
        var entity = Substitute.For<Entity>();
        EntityChangedDimensionEventArgs? captured = null;
        svc.EntityChangedDimension += (_, e) => captured = e;

        svc.TeleportEntity(entity, Code("owner:target"));

        Assert.NotNull(captured);
        Assert.Same(entity, captured!.Entity);
        Assert.Equal("owner:target", captured.NewDimension.Code.ToString());
        Assert.Equal(new BlockPos(100, 100, 100, captured.NewDimension.InternalId), captured.NewPosition);
    }

    [Fact]
    public void TeleportEntity_Should_Not_Raise_EntityChangedDimension_When_Mover_Throws()
    {
        var (svc, _, _, _, mover, _) = NewService();
        var entity = Substitute.For<Entity>();
        mover.When(m => m.Move(Arg.Any<Entity>(), Arg.Any<BlockPos>()))
             .Do(_ => throw new InvalidOperationException("boom"));
        bool raised = false;
        svc.EntityChangedDimension += (_, _) => raised = true;

        Assert.Throws<InvalidOperationException>(() => svc.TeleportEntity(entity, Code("owner:target")));
        Assert.False(raised);
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Target_Not_Found()
    {
        var (svc, _, _, _, _, _) = NewService();
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.Throws<DimensionNotFoundException>(() => svc.TeleportBlock(src, Code("nope:nope"), dst));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Target_Quarantined()
    {
        var (svc, registry, _, _, _, _) = NewService();
        var qCode = Code("ghost:bdim");
        registry.SeedFromManifest(
            new ManifestEntry(qCode, 97, DimensionLifetime.Persistent, "ghost"),
            DimensionState.Quarantined);
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.Throws<DimensionStateException>(() => svc.TeleportBlock(src, qCode, dst));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Unhealthy()
    {
        var (svc, _, _, _, _, _) = NewService();
        svc.MarkUnhealthy();
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.Throws<ManifoldUnhealthyException>(() => svc.TeleportBlock(src, Code("owner:target"), dst));
    }

    [Fact]
    public void TeleportBlock_Should_Call_Mover_With_Dim_Encoded_Target()
    {
        var (svc, registry, _, _, _, blockMover) = NewService();
        var target = registry.Get(Code("owner:target"))!;
        var src = new BlockPos(10, 64, 10, 0);
        var dst = new BlockPos(20, 64, 30, 0); // dim 0; service should overwrite to target.InternalId
        svc.TeleportBlock(src, Code("owner:target"), dst);
        blockMover.Received(1).Move(
            src,
            Arg.Is<BlockPos>(p => p.X == 20 && p.Y == 64 && p.Z == 30 && p.dimension == target.InternalId));
    }

    [Fact]
    public void TeleportBlock_Should_Not_Mutate_Caller_TargetPos()
    {
        var (svc, _, _, _, _, _) = NewService();
        var src = new BlockPos(10, 64, 10, 0);
        var dst = new BlockPos(20, 64, 30, 0);
        svc.TeleportBlock(src, Code("owner:target"), dst);
        Assert.Equal(0, dst.dimension);
    }

    [Fact]
    public void TeleportBlock_Should_Return_Mover_Result()
    {
        var (svc, _, _, _, _, blockMover) = NewService();
        blockMover.Move(Arg.Any<BlockPos>(), Arg.Any<BlockPos>()).Returns(false);
        var src = new BlockPos(1, 64, 1, 0);
        var dst = new BlockPos(2, 64, 2, 0);
        Assert.False(svc.TeleportBlock(src, Code("owner:target"), dst));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_Source_Is_Null()
    {
        var (svc, _, _, _, _, _) = NewService();
        Assert.Throws<ArgumentNullException>(() => svc.TeleportBlock(null!, Code("owner:target"), new BlockPos(1, 1, 1, 0)));
    }

    [Fact]
    public void TeleportBlock_Should_Throw_When_TargetLocal_Is_Null()
    {
        var (svc, _, _, _, _, _) = NewService();
        Assert.Throws<ArgumentNullException>(() => svc.TeleportBlock(new BlockPos(1, 1, 1, 0), Code("owner:target"), null!));
    }

    private static AssetLocation Code(string s) => new(s);

    private static (TransitService Service, DimensionRegistry Registry, IServerPlayer Player, IPlayerTeleporter Teleporter, IEntityMover EntityMover, IBlockMover BlockMover)
        NewService()
    {
        var allocator = new DimensionAllocator();
        var registry = new DimensionRegistry(allocator);
        registry.DefineForOwner(Code("owner:target"), "owner")
            .WithWorldgen(new FakeWorldgenStrategy())
            .RegisterStatic();

        var teleporter = Substitute.For<IPlayerTeleporter>();
        var positionResolver = Substitute.For<ITargetPositionResolver>();
        positionResolver
            .Resolve(Arg.Any<Entity>(), Arg.Any<IDimension>(), Arg.Any<ICoreServerAPI>())
            .Returns(new BlockPos(100, 100, 100, 10));

        var sapi = Substitute.For<ICoreServerAPI>();
        var generator = new DimensionGenerator(registry, new GeneratedColumnStore());
        var entityMover = Substitute.For<IEntityMover>();
        var blockMover = Substitute.For<IBlockMover>();
        blockMover.Move(Arg.Any<BlockPos>(), Arg.Any<BlockPos>()).Returns(true);
        var svc = new TransitService(
            registry,
            sapi,
            new TransitMovers(teleporter, entityMover, blockMover),
            positionResolver,
            generator,
            new PlayerPositionStore(),
            new InventorySwapper(sapi));

        var player = Substitute.For<IServerPlayer>();
        player.Entity.Returns(Substitute.For<EntityPlayer>());

        return (svc, registry, player, teleporter, entityMover, blockMover);
    }
}
